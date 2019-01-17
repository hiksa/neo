using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using Neo.Wallets;

namespace Neo.Consensus
{
    public sealed class ConsensusService : UntypedActor
    {
        private readonly IConsensusContext context;
        private readonly IActorRef localNodeActorRef;
        private readonly IActorRef taskManagerActorRef;
        private ICancelable timerToken;
        private DateTime blockReceivedTime;

        public ConsensusService(IActorRef localNode, IActorRef taskManager, Wallet wallet)
            : this(localNode, taskManager, new ConsensusContext(wallet))
        {
        }

        public ConsensusService(IActorRef localNode, IActorRef taskManager, IConsensusContext context)
        {
            this.localNodeActorRef = localNode;
            this.taskManagerActorRef = taskManager;
            this.context = context;
        }

        public static Props Props(IActorRef localNode, IActorRef taskManager, Wallet wallet) =>
            Akka.Actor.Props
                .Create(() => new ConsensusService(localNode, taskManager, wallet))
                .WithMailbox("consensus-service-mailbox");

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Start _:
                    this.OnStart();
                    break;
                case SetViewNumber setView:
                    this.InitializeConsensus(setView.ViewNumber);
                    break;
                case Timer timer:
                    this.OnTimer(timer);
                    break;
                case ConsensusPayload payload:
                    this.OnConsensusPayload(payload);
                    break;
                case Transaction transaction:
                    this.OnTransaction(transaction);
                    break;
                case Blockchain.PersistCompleted completed:
                    this.OnPersistCompleted(completed.Block);
                    break;
            }
        }

        protected override void PostStop()
        {
            this.Log("OnStop");
            this.context.Dispose();
            base.PostStop();
        }

        private void RequestChangeView()
        {
            this.context.State |= ConsensusStates.ViewChanging;
            this.context.ExpectedView[this.context.MyIndex]++;

            this.Log($"request change view: height={this.context.BlockIndex} view={this.context.ViewNumber} nv={this.context.ExpectedView[this.context.MyIndex]} state={this.context.State}");

            var delayInSeconds = Blockchain.SecondsPerBlock << (this.context.ExpectedView[this.context.MyIndex] + 1);
            var delay = TimeSpan.FromSeconds(delayInSeconds);

            this.ChangeTimer(delay);

            var sendDirectlyMessage = new LocalNode.SendDirectly(this.context.MakeChangeView());
            this.localNodeActorRef.Tell(sendDirectlyMessage);
            this.CheckExpectedView(this.context.ExpectedView[this.context.MyIndex]);
        }

        private bool AddTransaction(Transaction tx, bool verify)
        {
            if (verify && !this.context.VerifyTransaction(tx))
            {
                this.Log($"Invalid transaction: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", LogLevel.Warning);
                this.RequestChangeView();
                return false;
            }

            if (!Plugin.CheckPolicy(tx))
            {
                this.Log($"reject tx: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", LogLevel.Warning);
                this.RequestChangeView();
                return false;
            }

            this.context.Transactions[tx.Hash] = tx;
            if (this.context.TransactionHashes.Length == this.context.Transactions.Count)
            {
                if (this.context.VerifyRequest())
                {
                    this.Log($"send prepare response");

                    this.context.State |= ConsensusStates.SignatureSent;
                    this.context.SignHeader();

                    var currentNodeSignature = this.context.Signatures[this.context.MyIndex];
                    var sendDirectlyPayload = this.context.MakePrepareResponse(currentNodeSignature);
                    var sendDirectlyMessage = new LocalNode.SendDirectly(sendDirectlyPayload);

                    this.localNodeActorRef.Tell(sendDirectlyMessage);
                    this.CheckSignatures();
                }
                else
                {
                    this.RequestChangeView();
                    return false;
                }
            }

            return true;
        }

        private void ChangeTimer(TimeSpan delay)
        {
            this.timerToken.CancelIfNotNull();
            this.timerToken = Context.System.Scheduler.ScheduleTellOnceCancelable(
                delay,
                this.Self,
                new Timer(this.context.BlockIndex, this.context.ViewNumber),
                ActorRefs.NoSender);
        }

        private void CheckExpectedView(byte viewNumber)
        {
            if (this.context.ViewNumber == viewNumber)
            {
                return;
            }

            if (this.context.ExpectedView.Count(p => p == viewNumber) >= this.context.M)
            {
                this.InitializeConsensus(viewNumber);
            }
        }

        private void CheckSignatures()
        {
            var signaturesCount = this.context.Signatures.Count(p => p != null);
            var allHashesAreCorrect = this.context.TransactionHashes.All(p => this.context.Transactions.ContainsKey(p));
            if (signaturesCount >= this.context.M && allHashesAreCorrect)
            {
                var block = this.context.CreateBlock();
                this.Log($"relay block: {block.Hash}");

                this.localNodeActorRef.Tell(new LocalNode.Relay(block));
                this.context.State |= ConsensusStates.BlockSent;
            }
        }

        private void InitializeConsensus(byte viewNumber)
        {
            if (viewNumber == 0)
            {
                this.context.Reset();
            }
            else
            {
                this.context.ChangeView(viewNumber);
            }

            if (this.context.MyIndex < 0)
            {
                return;
            }

            if (viewNumber > 0)
            {
                this.Log($"changeview: view={viewNumber} primary={this.context.Validators[this.context.GetPrimaryIndex((byte)(viewNumber - 1u))]}", LogLevel.Warning);
            }

            var nodeRole = this.context.MyIndex == this.context.PrimaryIndex 
                ? ConsensusStates.Primary 
                : ConsensusStates.Backup;

            this.Log($"initialize: height={this.context.BlockIndex} view={viewNumber} index={this.context.MyIndex} role={nodeRole}");

            if (this.context.MyIndex == this.context.PrimaryIndex)
            {
                this.context.State |= ConsensusStates.Primary;
                var timeSinceLastBlock = TimeProvider.Current.UtcNow - this.blockReceivedTime;
                if (timeSinceLastBlock >= Blockchain.TimePerBlock)
                {
                    this.ChangeTimer(TimeSpan.Zero);
                }
                else
                {
                    this.ChangeTimer(Blockchain.TimePerBlock - timeSinceLastBlock);
                }
            }
            else
            {
                this.context.State = ConsensusStates.Backup;

                var timerSeonds = Blockchain.SecondsPerBlock << (viewNumber + 1);
                this.ChangeTimer(TimeSpan.FromSeconds(timerSeonds));
            }
        }

        private void Log(string message, LogLevel level = LogLevel.Info) =>
            Plugin.Log(nameof(ConsensusService), level, message);

        private void OnStart()
        {
            this.Log("OnStart");
            this.InitializeConsensus(0);
        }

        private void OnTimer(Timer timer)
        {
            if (this.context.State.HasFlag(ConsensusStates.BlockSent))
            {
                return;
            }

            if (timer.Height != this.context.BlockIndex || timer.ViewNumber != this.context.ViewNumber)
            {
                return;
            }

            this.Log($"timeout: height={timer.Height} view={timer.ViewNumber} state={this.context.State}");
            if (this.context.State.HasFlag(ConsensusStates.Primary) && !this.context.State.HasFlag(ConsensusStates.RequestSent))
            {
                this.Log($"send prepare request: height={timer.Height} view={timer.ViewNumber}");
                this.context.State |= ConsensusStates.RequestSent;
                if (!this.context.State.HasFlag(ConsensusStates.SignatureSent))
                {
                    this.context.Fill();
                    this.context.SignHeader();
                }

                var consensusPayload = this.context.MakePrepareRequest();
                var consensusMessage = new LocalNode.SendDirectly(consensusPayload);
                this.localNodeActorRef.Tell(consensusMessage);

                if (this.context.TransactionHashes.Length > 1)
                {
                    var transactionHashes = this.context.TransactionHashes.Skip(1).ToArray();
                    var payloads = InvPayload.CreateMany(InventoryType.TX, transactionHashes);
                    foreach (var invPayload in payloads)
                    {
                        var message = Message.Create("inv", invPayload);
                        this.localNodeActorRef.Tell(message);
                    }
                }

                var timerSeconds = Blockchain.SecondsPerBlock << (timer.ViewNumber + 1);
                this.ChangeTimer(TimeSpan.FromSeconds(timerSeconds));
            }
            else if (this.context.State.HasAllFlags(ConsensusStates.Primary, ConsensusStates.RequestSent)
                || this.context.State.HasFlag(ConsensusStates.Backup))
            {
                this.RequestChangeView();
            }
        }

        private void OnTransaction(Transaction transaction)
        {
            if (transaction.Type == TransactionType.MinerTransaction
                || !this.context.State.HasFlag(ConsensusStates.Backup)
                || !this.context.State.HasFlag(ConsensusStates.RequestReceived)
                || this.context.State.HasFlag(ConsensusStates.SignatureSent)
                || this.context.State.HasFlag(ConsensusStates.ViewChanging)
                || this.context.State.HasFlag(ConsensusStates.BlockSent)
                || this.context.Transactions.ContainsKey(transaction.Hash)
                || !this.context.TransactionHashes.Contains(transaction.Hash))
            {
                return;
            }

            this.AddTransaction(transaction, true);
        }

        private void OnChangeViewReceived(ConsensusPayload payload, ChangeView message)
        {
            if (message.NewViewNumber <= this.context.ExpectedView[payload.ValidatorIndex])
            {
                return;
            }

            this.Log($"{nameof(this.OnChangeViewReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} nv={message.NewViewNumber}");

            this.context.ExpectedView[payload.ValidatorIndex] = message.NewViewNumber;

            this.CheckExpectedView(message.NewViewNumber);
        }

        private void OnConsensusPayload(ConsensusPayload payload)
        {
            if (this.context.State.HasFlag(ConsensusStates.BlockSent)
                || payload.ValidatorIndex == this.context.MyIndex
                || payload.Version != ConsensusContext.Version)
            {
                return;
            }

            if (payload.PrevHash != this.context.PreviousBlockHash 
                || payload.BlockIndex != this.context.BlockIndex)
            {
                if (this.context.BlockIndex < payload.BlockIndex)
                {
                    this.Log($"chain sync: expected={payload.BlockIndex} current={this.context.BlockIndex - 1} nodes={LocalNode.Instance.ConnectedCount}", LogLevel.Warning);
                }

                return;
            }

            if (payload.ValidatorIndex >= this.context.Validators.Length)
            {
                return;
            }

            ConsensusMessage message;
            try
            {
                message = ConsensusMessage.DeserializeFrom(payload.Data);
            }
            catch
            {
                return;
            }

            if (message.ViewNumber != this.context.ViewNumber && message.Type != ConsensusMessageType.ChangeView)
            {
                return;
            }

            switch (message.Type)
            {
                case ConsensusMessageType.ChangeView:
                    this.OnChangeViewReceived(payload, (ChangeView)message);
                    break;
                case ConsensusMessageType.PrepareRequest:
                    this.OnPrepareRequestReceived(payload, (PrepareRequest)message);
                    break;
                case ConsensusMessageType.PrepareResponse:
                    this.OnPrepareResponseReceived(payload, (PrepareResponse)message);
                    break;
            }
        }

        private void OnPersistCompleted(Block block)
        {
            this.Log($"persist block: {block.Hash}");
            this.blockReceivedTime = TimeProvider.Current.UtcNow;

            this.InitializeConsensus(0);
        }

        private void OnPrepareRequestReceived(ConsensusPayload payload, PrepareRequest message)
        {
            if (this.context.State.HasFlag(ConsensusStates.RequestReceived))
            {
                return;
            }

            if (payload.ValidatorIndex != this.context.PrimaryIndex)
            {
                return;
            }

            this.Log($"{nameof(OnPrepareRequestReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} tx={message.TransactionHashes.Length}");
            if (!this.context.State.HasFlag(ConsensusStates.Backup))
            {
                return;
            }

            if (payload.Timestamp <= this.context.PrevHeader.Timestamp 
                || payload.Timestamp > TimeProvider.Current.UtcNow.AddMinutes(10).ToTimestamp())
            {
                this.Log($"Timestamp incorrect: {payload.Timestamp}", LogLevel.Warning);
                return;
            }

            if (message.TransactionHashes.Any(p => this.context.TransactionExists(p)))
            {
                this.Log($"Invalid request: transaction already exists", LogLevel.Warning);
                return;
            }

            this.context.State |= ConsensusStates.RequestReceived;
            this.context.Timestamp = payload.Timestamp;
            this.context.Nonce = message.Nonce;
            this.context.NextConsensus = message.NextConsensus;
            this.context.TransactionHashes = message.TransactionHashes;
            this.context.Transactions = new Dictionary<UInt256, Transaction>();

            var hashData = this.context.MakeHeader().GetHashData();
            var publicKey = this.context.Validators[payload.ValidatorIndex].EncodePoint(false);
            var siangureIsValid = Crypto.Default.VerifySignature(hashData, message.Signature, publicKey);
            if (!siangureIsValid)
            {
                return;
            }

            for (var i = 0; i < this.context.Signatures.Length; i++)
            {
                if (this.context.Signatures[i] != null)
                {
                    var signature = this.context.Signatures[i];
                    var currentPublicKey = this.context.Validators[i].EncodePoint(false);
                    if (!Crypto.Default.VerifySignature(hashData, signature, currentPublicKey))
                    {
                        this.context.Signatures[i] = null;
                    }
                }
            }

            this.context.Signatures[payload.ValidatorIndex] = message.Signature;

            var mempool = Blockchain.Instance.GetMemoryPool().ToDictionary(p => p.Hash);
            var unverified = new List<Transaction>();

            var transactionHashes = this.context.TransactionHashes.Skip(1);
            foreach (var hash in transactionHashes)
            {
                if (mempool.TryGetValue(hash, out Transaction tx))
                {
                    if (!this.AddTransaction(tx, false))
                    {
                        return;
                    }
                }
                else
                {
                    tx = Blockchain.Instance.GetUnverifiedTransaction(hash);
                    if (tx != null)
                    {
                        unverified.Add(tx);
                    }
                }
            }

            foreach (var tx in unverified)
            {
                if (!this.AddTransaction(tx, true))
                {
                    return;
                }
            }

            if (!this.AddTransaction(message.MinerTransaction, true))
            {
                return;
            }

            if (this.context.Transactions.Count < this.context.TransactionHashes.Length)
            {
                var hashes = this.context.TransactionHashes
                    .Where(i => !this.context.Transactions.ContainsKey(i))
                    .ToArray();

                var invPayload = InvPayload.Create(InventoryType.TX, hashes);
                var restartTasksMessage = new TaskManager.RestartTasks(invPayload);
                this.taskManagerActorRef.Tell(restartTasksMessage);
            }
        }

        private void OnPrepareResponseReceived(ConsensusPayload payload, PrepareResponse message)
        {
            if (this.context.Signatures[payload.ValidatorIndex] != null)
            {
                return;
            }

            this.Log($"{nameof(OnPrepareResponseReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex}");

            var hashData = this.context.MakeHeader()?.GetHashData();
            if (hashData == null)
            {
                this.context.Signatures[payload.ValidatorIndex] = message.Signature;
                return;
            }

            var publicKey = this.context.Validators[payload.ValidatorIndex].EncodePoint(false);
            if (Crypto.Default.VerifySignature(hashData, message.Signature, publicKey))
            {
                this.context.Signatures[payload.ValidatorIndex] = message.Signature;
                this.CheckSignatures();
            }
        }

        public class Start
        {
        }

        public class SetViewNumber
        {
            public SetViewNumber(byte viewNumber)
            {
                this.ViewNumber = viewNumber;
            }

            public byte ViewNumber { get; private set; }
        }

        internal class Timer
        {
            public Timer(uint height, byte viewNumber)
            {
                this.Height = height;
                this.ViewNumber = viewNumber;
            }

            public uint Height { get; private set; }

            public byte ViewNumber { get; private set; }
        }
    }
}
