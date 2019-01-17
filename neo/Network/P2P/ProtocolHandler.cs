using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;

namespace Neo.Network.P2P
{
    internal class ProtocolHandler : UntypedActor
    {
        private readonly NeoSystem system;
        private readonly HashSet<UInt256> knownHashes = new HashSet<UInt256>();
        private readonly HashSet<UInt256> sentHashes = new HashSet<UInt256>();

        private VersionPayload version;
        private BloomFilter bloomFilter;
        private bool verack = false;

        public ProtocolHandler(NeoSystem system)
        {
            this.system = system;
        }
        
        public static Props Props(NeoSystem system) =>
            Akka.Actor.Props.Create(() => new ProtocolHandler(system)).WithMailbox("protocol-handler-mailbox");

        protected override void OnReceive(object message)
        {
            if (!(message is Message msg))
            {
                return;
            }

            if (this.version == null)
            {
                if (msg.Command != "version")
                {
                    throw new ProtocolViolationException();
                }

                this.OnVersionMessageReceived(msg.Payload.AsSerializable<VersionPayload>());
                return;
            }

            if (!this.verack)
            {
                if (msg.Command != "verack")
                {
                    throw new ProtocolViolationException();
                }

                this.OnVerackMessageReceived();
                return;
            }

            switch (msg.Command)
            {
                case "addr":
                    this.OnAddrMessageReceived(msg.Payload.AsSerializable<AddrPayload>());
                    break;
                case "block":
                    this.OnInventoryReceived(msg.Payload.AsSerializable<Block>());
                    break;
                case "consensus":
                    this.OnInventoryReceived(msg.Payload.AsSerializable<ConsensusPayload>());
                    break;
                case "filteradd":
                    this.OnFilterAddMessageReceived(msg.Payload.AsSerializable<FilterAddPayload>());
                    break;
                case "filterclear":
                    this.OnFilterClearMessageReceived();
                    break;
                case "filterload":
                    this.OnFilterLoadMessageReceived(msg.Payload.AsSerializable<FilterLoadPayload>());
                    break;
                case "getaddr":
                    this.OnGetAddrMessageReceived();
                    break;
                case "getblocks":
                    this.OnGetBlocksMessageReceived(msg.Payload.AsSerializable<GetBlocksPayload>());
                    break;
                case "getdata":
                    this.OnGetDataMessageReceived(msg.Payload.AsSerializable<InvPayload>());
                    break;
                case "getheaders":
                    this.OnGetHeadersMessageReceived(msg.Payload.AsSerializable<GetBlocksPayload>());
                    break;
                case "headers":
                    this.OnHeadersMessageReceived(msg.Payload.AsSerializable<HeadersPayload>());
                    break;
                case "inv":
                    this.OnInvMessageReceived(msg.Payload.AsSerializable<InvPayload>());
                    break;
                case "mempool":
                    this.OnMemPoolMessageReceived();
                    break;
                case "tx":
                    if (msg.Payload.Length <= Transaction.MaxTransactionSize)
                    {
                        this.OnInventoryReceived(Transaction.DeserializeFrom(msg.Payload));
                    }

                    break;
                case "verack":
                case "version":
                    throw new ProtocolViolationException();
                case "alert":
                case "merkleblock":
                case "notfound":
                case "ping":
                case "pong":
                case "reject":
                default:
                    // 暂时忽略
                    break;
            }
        }

        private void OnAddrMessageReceived(AddrPayload payload)
        {
            var peersMessage = new Peer.Peers(payload.AddressList.Select(p => p.EndPoint));
            this.system.LocalNodeActorRef.Tell(peersMessage);
        }

        private void OnFilterAddMessageReceived(FilterAddPayload payload)
        {
            if (this.bloomFilter != null)
            {
                this.bloomFilter.Add(payload.Data);
            }
        }

        private void OnFilterClearMessageReceived()
        {
            this.bloomFilter = null;
            UntypedActor.Context.Parent.Tell(new SetFilter(null));
        }

        private void OnFilterLoadMessageReceived(FilterLoadPayload payload)
        {
            this.bloomFilter = new BloomFilter(
                payload.Filter.Length * 8, 
                payload.K, 
                payload.Tweak, 
                payload.Filter);

            var setFilterMessage = new SetFilter(this.bloomFilter);
            UntypedActor.Context.Parent.Tell(setFilterMessage);
        }

        private void OnGetAddrMessageReceived()
        {
            var random = new Random();
            var peers = LocalNode.Instance
                .RemoteNodes
                .Values
                .Where(p => p.ListenerPort > 0)
                .GroupBy(p => p.Remote.Address, (k, g) => g.First())
                .OrderBy(p => random.Next())
                .Take(AddrPayload.MaxCountToSend);

            var networkAddresses = peers
                .Select(p => NetworkAddressWithTime.Create(p.Listener, p.Version.Services, p.Version.Timestamp))
                .ToArray();

            if (networkAddresses.Length == 0)
            {
                return;
            }

            var addrMessage = Message.Create("addr", AddrPayload.Create(networkAddresses));
            UntypedActor.Context.Parent.Tell(addrMessage);
        }

        private void OnGetBlocksMessageReceived(GetBlocksPayload payload)
        {
            var hash = payload.HashStart[0];
            if (hash == payload.HashStop)
            {
                return;
            }

            var blockState = Blockchain.Instance
                .Store
                .GetBlocks()
                .TryGet(hash);

            if (blockState == null)
            {
                return;
            }

            var hashes = new List<UInt256>();
            for (uint i = 1; i <= InvPayload.MaxHashesCount; i++)
            {
                uint index = blockState.TrimmedBlock.Index + i;
                if (index > Blockchain.Instance.Height)
                {
                    break;
                }

                hash = Blockchain.Instance.GetBlockHash(index);
                if (hash == null)
                {
                    break;
                }

                if (hash == payload.HashStop)
                {
                    break;
                }

                hashes.Add(hash);
            }

            if (hashes.Count == 0)
            {
                return;
            }

            var invPayload = InvPayload.Create(InventoryType.Block, hashes.ToArray());
            var invMessage = Message.Create("inv", invPayload);

            UntypedActor.Context.Parent.Tell(invMessage);
        }

        private void OnGetDataMessageReceived(InvPayload payload)
        {
            var hashes = payload.Hashes.Where(p => this.sentHashes.Add(p)).ToArray();
            foreach (var hash in hashes)
            {
                Blockchain.Instance.RelayCache.TryGet(hash, out IInventory inventory);
                switch (payload.Type)
                {
                    case InventoryType.TX:
                        if (inventory == null)
                        {
                            inventory = Blockchain.Instance.GetTransaction(hash);
                        }

                        if (inventory is Transaction)
                        {
                            var transactionMessage = Message.Create("tx", inventory);
                            UntypedActor.Context.Parent.Tell(transactionMessage);
                        }

                        break;
                    case InventoryType.Block:
                        if (inventory == null)
                        {
                            inventory = Blockchain.Instance.GetBlock(hash);
                        }

                        if (inventory is Block block)
                        {
                            if (this.bloomFilter == null)
                            {
                                var blockMessage = Message.Create("block", inventory);
                                Context.Parent.Tell(blockMessage);
                            }
                            else
                            {
                                var bits = block.Transactions.Select(p => this.bloomFilter.Test(p)).ToArray();
                                var flags = new BitArray(bits);

                                var merkleBlockPayload = MerkleBlockPayload.Create(block, flags);
                                var merkleBlockMessage = Message.Create("merkleblock", merkleBlockPayload);
                                UntypedActor.Context.Parent.Tell(merkleBlockMessage);
                            }
                        }

                        break;
                    case InventoryType.Consensus:
                        if (inventory != null)
                        {
                            var consensusMessage = Message.Create("consensus", inventory);
                            UntypedActor.Context.Parent.Tell(consensusMessage);
                        }

                        break;
                }
            }
        }

        private void OnGetHeadersMessageReceived(GetBlocksPayload payload)
        {
            var hash = payload.HashStart[0];
            if (hash == payload.HashStop)
            {
                return;
            }

            var blocksCache = Blockchain.Instance.Store.GetBlocks();
            var blockState = blocksCache.TryGet(hash);
            if (blockState == null)
            {
                return;
            }

            var headers = new List<Header>();
            for (uint i = 1; i <= HeadersPayload.MaxHeadersCount; i++)
            {
                uint index = blockState.TrimmedBlock.Index + i;
                hash = Blockchain.Instance.GetBlockHash(index);
                if (hash == null)
                {
                    break;
                }

                if (hash == payload.HashStop)
                {
                    break;
                }

                var header = blocksCache.TryGet(hash)?.TrimmedBlock.Header;
                if (header == null)
                {
                    break;
                }

                headers.Add(header);
            }

            if (headers.Count == 0)
            {
                return;
            }

            var headersPayload = HeadersPayload.Create(headers);
            var headersMessage = Message.Create("headers", headersPayload);
            UntypedActor.Context.Parent.Tell(headersMessage);
        }

        private void OnHeadersMessageReceived(HeadersPayload payload)
        {
            if (payload.Headers.Length == 0)
            {
                return;
            }

            this.system.BlockchainActorRef.Tell(payload.Headers, Context.Parent);
        }

        private void OnInventoryReceived(IInventory inventory)
        {
            var taskCompletedMessage = new TaskManager.TaskCompleted(inventory.Hash);
            this.system.TaskManagerActorRef.Tell(taskCompletedMessage, UntypedActor.Context.Parent);
            if (inventory is MinerTransaction)
            {
                return;
            }

            var relayMessage = new LocalNode.Relay(inventory);
            this.system.LocalNodeActorRef.Tell(relayMessage);
        }

        private void OnInvMessageReceived(InvPayload payload)
        {
            var hashes = payload.Hashes.Where(p => this.knownHashes.Add(p)).ToArray();
            if (hashes.Length == 0)
            {
                return;
            }

            switch (payload.Type)
            {
                case InventoryType.Block:
                    using (var snapshot = Blockchain.Instance.GetSnapshot())
                    {
                        hashes = hashes.Where(p => !snapshot.ContainsBlock(p)).ToArray();
                    }

                    break;
                case InventoryType.TX:
                    using (var snapshot = Blockchain.Instance.GetSnapshot())
                    {
                        hashes = hashes.Where(p => !snapshot.ContainsTransaction(p)).ToArray();
                    }

                    break;
            }

            if (hashes.Length == 0)
            {
                return;
            }

            var newTasksPayload = InvPayload.Create(payload.Type, hashes);
            var newTasksMessage = new TaskManager.NewTasks(newTasksPayload);

            this.system.TaskManagerActorRef.Tell(newTasksMessage, Context.Parent);
        }

        private void OnMemPoolMessageReceived()
        {
            var memPoolTransactionHashes = Blockchain.Instance
                .GetMemoryPool()
                .Select(p => p.Hash)
                .ToArray();

            var payloads = InvPayload.CreateMany(InventoryType.TX, memPoolTransactionHashes);
            foreach (var payload in payloads)
            {
                var invMessage = Message.Create("inv", payload);
                Context.Parent.Tell(invMessage);
            }
        }

        private void OnVerackMessageReceived()
        {
            this.verack = true;
            Context.Parent.Tell(new SetVerack());
        }

        private void OnVersionMessageReceived(VersionPayload payload)
        {
            this.version = payload;
            Context.Parent.Tell(new SetVersion(payload));
        }

        public class SetVersion
        {
            public SetVersion(VersionPayload version)
            {
                this.Version = version;
            }

            public VersionPayload Version { get; private set; }
        }

        public class SetVerack
        {
        }

        public class SetFilter
        {
            public SetFilter(BloomFilter filter)
            {
                this.Filter = filter;
            }

            public BloomFilter Filter { get; private set; }
        }
    }
}
