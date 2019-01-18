using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.IO;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    public class RemoteNode : Connection
    {
        private readonly NeoSystem system;
        private readonly IActorRef protocol;
        private readonly Queue<Message> messageQueueHigh = new Queue<Message>();
        private readonly Queue<Message> messageQueueLow = new Queue<Message>();
        private ByteString messageBuffer = ByteString.Empty;
        private bool ack = true;
        private BloomFilter bloomFilter;
        private bool verack = false;

        public RemoteNode(NeoSystem system, object connection, IPEndPoint remote, IPEndPoint local)
            : base(connection, remote, local)
        {
            this.system = system;
            this.protocol = Context.ActorOf(ProtocolHandler.Props(system));
            LocalNode.Instance.RemoteNodes.TryAdd(this.Self, this);

            var versionPayload = VersionPayload.Create(
                LocalNode.Instance.ListenerPort, 
                LocalNode.Nonce, 
                LocalNode.UserAgent, 
                Blockchain.Instance.Height);
            var versionMessage = Message.Create("version", versionPayload);
            this.SendMessage(versionMessage);
        }
        
        public IPEndPoint Listener => new IPEndPoint(this.Remote.Address, this.ListenerPort);

        public override int ListenerPort => this.Version?.Port ?? 0;

        public VersionPayload Version { get; private set; }

        internal static Props Props(NeoSystem system, object connection, IPEndPoint remote, IPEndPoint local) =>
            Akka.Actor.Props
                .Create(() => new RemoteNode(system, connection, remote, local))
                .WithMailbox("remote-node-mailbox");
        
        protected override void OnAck()
        {
            this.ack = true;
            this.CheckMessageQueue();
        }

        protected override void OnData(ByteString data)
        {
            this.messageBuffer = this.messageBuffer.Concat(data);
            for (var message = this.TryParseMessage(); message != null; message = this.TryParseMessage())
            {
                this.protocol.Tell(message);
            }
        }

        protected override void OnReceive(object message)
        {
            base.OnReceive(message);

            switch (message)
            {
                case Message msg:
                    this.EnqueueMessage(msg);
                    break;
                case IInventory inventory:
                    this.OnSend(inventory);
                    break;
                case Relay relay:
                    this.OnRelay(relay.Inventory);
                    break;
                case ProtocolHandler.SetVersion setVersion:
                    this.OnSetVersion(setVersion.Version);
                    break;
                case ProtocolHandler.SetVerack _:
                    this.OnSetVerack();
                    break;
                case ProtocolHandler.SetFilter setFilter:
                    this.OnSetFilter(setFilter.Filter);
                    break;
            }
        }

        protected override void PostStop()
        {
            LocalNode.Instance.RemoteNodes.TryRemove(this.Self, out _);
            base.PostStop();
        }

        protected override SupervisorStrategy SupervisorStrategy() =>
            new OneForOneStrategy(
                ex =>
                {
                    this.Disconnect(true);
                    return Directive.Stop;
                },
                loggingEnabled: false);

        private void CheckMessageQueue()
        {
            if (!this.verack || !this.ack)
            {
                return;
            }

            var messagesQueue = this.messageQueueHigh;
            if (messagesQueue.Count == 0)
            {
                messagesQueue = this.messageQueueLow;
            }

            if (messagesQueue.Count == 0)
            {
                return;
            }

            this.SendMessage(messagesQueue.Dequeue());
        }

        private void EnqueueMessage(string command, ISerializable payload = null) =>
            this.EnqueueMessage(Message.Create(command, payload));

        private void EnqueueMessage(Message message)
        {
            var isSingle = false;
            switch (message.Command)
            {
                case "addr":
                case "getaddr":
                case "getblocks":
                case "getheaders":
                case "mempool":
                    isSingle = true;
                    break;
            }

            Queue<Message> messagesQueue;
            switch (message.Command)
            {
                case "alert":
                case "consensus":
                case "filteradd":
                case "filterclear":
                case "filterload":
                case "getaddr":
                case "mempool":
                    messagesQueue = this.messageQueueHigh;
                    break;
                default:
                    messagesQueue = this.messageQueueLow;
                    break;
            }

            if (!isSingle || messagesQueue.All(p => p.Command != message.Command))
            {
                messagesQueue.Enqueue(message);
            }

            this.CheckMessageQueue();
        }

        private void OnRelay(IInventory inventory)
        {
            if (this.Version?.Relay != true)
            {
                return;
            }

            if (inventory.InventoryType == InventoryType.TX)
            {
                if (this.bloomFilter != null && !this.bloomFilter.Test((Transaction)inventory))
                {
                    return;
                }
            }

            var invPayload = InvPayload.Create(inventory.InventoryType, inventory.Hash);
            this.EnqueueMessage("inv", invPayload);
        }

        private void OnSend(IInventory inventory)
        {
            if (this.Version?.Relay != true)
            {
                return;
            }

            if (inventory.InventoryType == InventoryType.TX)
            {
                if (this.bloomFilter != null && !this.bloomFilter.Test((Transaction)inventory))
                {
                    return;
                }
            }

            var message = inventory
                .InventoryType
                .ToString()
                .ToLower();

            this.EnqueueMessage(message, inventory);
        }

        private void OnSetFilter(BloomFilter filter) => this.bloomFilter = filter;

        private void OnSetVerack()
        {
            this.verack = true;

            var registerMessage = new TaskManager.Register(this.Version);
            this.system.TaskManagerActorRef.Tell(registerMessage);
            this.CheckMessageQueue();
        }

        private void OnSetVersion(VersionPayload version)
        {
            this.Version = version;
            if (version.Nonce == LocalNode.Nonce)
            {
                this.Disconnect(true);
                return;
            }

            var alreadyConnected = LocalNode
                .Instance
                .RemoteNodes
                .Values
                .Where(p => p != this)
                .Any(p => p.Remote.Address.Equals(this.Remote.Address) && p.Version?.Nonce == version.Nonce);

            if (alreadyConnected)
            {
                this.Disconnect(true);
                return;
            }

            var verackMessage = Message.Create("verack");
            this.SendMessage(verackMessage);
        }

        private void SendMessage(Message message)
        {
            this.ack = false;

            var data = ByteString.FromBytes(message.ToArray());
            this.SendData(data);
        }       

        private Message TryParseMessage()
        {
            if (this.messageBuffer.Count < sizeof(uint))
            {
                return null;
            }

            var magic = this.messageBuffer
                .Slice(0, sizeof(uint))
                .ToArray()
                .ToUInt32(0);

            if (magic != Message.Magic)
            {
                throw new FormatException();
            }

            if (this.messageBuffer.Count < Message.HeaderSize)
            {
                return null;
            }

            var length = this.messageBuffer
                .Slice(16, sizeof(int))
                .ToArray()
                .ToInt32(0);

            if (length > Message.PayloadMaxSize)
            {
                throw new FormatException();
            }

            length += Message.HeaderSize;
            if (this.messageBuffer.Count < length)
            {
                return null;
            }

            var message = this.messageBuffer
                .Slice(0, length)
                .ToArray()
                .AsSerializable<Message>();

            this.messageBuffer = this.messageBuffer.Slice(length).Compact();
            return message;
        }

        internal class Relay
        {
            public Relay(IInventory inventory)
            {
                this.Inventory = inventory;
            }

            public IInventory Inventory { get; private set; }
        }
    }
}
