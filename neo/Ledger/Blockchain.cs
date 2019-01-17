using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Akka.Actor;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger.States;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.Ledger
{
    public sealed class Blockchain : UntypedActor
    {
        public const uint DecrementInterval = 2000000;
        public const uint MaxValidators = 1024;

        public static readonly uint SecondsPerBlock = ProtocolSettings.Default.SecondsPerBlock;
        public static readonly uint[] GenerationAmount =
        {
            8, 7, 6, 5, 4, 3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
        };

        public static readonly TimeSpan TimePerBlock = TimeSpan.FromSeconds(SecondsPerBlock);
        public static readonly ECPoint[] StandbyValidators = ProtocolSettings.Default
            .StandbyValidators
            .OfType<string>()
            .Select(p => ECPoint.DecodePoint(p.HexToBytes(), ECCurve.Secp256r1))
            .ToArray();

#pragma warning disable CS0612
        public static readonly RegisterTransaction GoverningToken = new RegisterTransaction
        {
            AssetType = AssetType.GoverningToken,
            Name = "[{\"lang\":\"zh-CN\",\"name\":\"小蚁股\"},{\"lang\":\"en\",\"name\":\"AntShare\"}]",
            Amount = Fixed8.FromDecimal(100000000),
            Precision = 0,
            Owner = ECCurve.Secp256r1.Infinity,
            Admin = (new[] { (byte)OpCode.PUSHT }).ToScriptHash(),
            Attributes = new TransactionAttribute[0],
            Inputs = new CoinReference[0],
            Outputs = new TransactionOutput[0],
            Witnesses = new Witness[0]
        };

        public static readonly RegisterTransaction UtilityToken = new RegisterTransaction
        {
            AssetType = AssetType.UtilityToken,
            Name = "[{\"lang\":\"zh-CN\",\"name\":\"小蚁币\"},{\"lang\":\"en\",\"name\":\"AntCoin\"}]",
            Amount = Fixed8.FromDecimal(Blockchain.GenerationAmount.Sum(p => p) * Blockchain.DecrementInterval),
            Precision = 8,
            Owner = ECCurve.Secp256r1.Infinity,
            Admin = (new[] { (byte)OpCode.PUSHF }).ToScriptHash(),
            Attributes = new TransactionAttribute[0],
            Inputs = new CoinReference[0],
            Outputs = new TransactionOutput[0],
            Witnesses = new Witness[0]
        };
#pragma warning restore CS0612

        public static readonly Block GenesisBlock = new Block
        {
            PrevHash = UInt256.Zero,
            Timestamp = (new DateTime(2016, 7, 15, 15, 8, 21, DateTimeKind.Utc)).ToTimestamp(),
            Index = 0,
            ConsensusData = 2083236893, // 向比特币致敬
            NextConsensus = Blockchain.GetConsensusAddress(Blockchain.StandbyValidators),
            Witness = new Witness
            {
                InvocationScript = new byte[0],
                VerificationScript = new[] { (byte)OpCode.PUSHT }
            },
            Transactions = new Transaction[]
            {
                new MinerTransaction
                {
                    Nonce = 2083236893,
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = new TransactionOutput[0],
                    Witnesses = new Witness[0]
                },
                GoverningToken,
                UtilityToken,
                new IssueTransaction
                {
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = new[]
                    {
                        new TransactionOutput
                        {
                            AssetId = GoverningToken.Hash,
                            Value = GoverningToken.Amount,
                            ScriptHash = Contract
                                .CreateMultiSigRedeemScript((Blockchain.StandbyValidators.Length / 2) + 1, Blockchain.StandbyValidators)
                                .ToScriptHash()
                        }
                    },
                    Witnesses = new[]
                    {
                        new Witness
                        {
                            InvocationScript = new byte[0],
                            VerificationScript = new[] { (byte)OpCode.PUSHT }
                        }
                    }
                }
            }
        };

        internal readonly RelayCache RelayCache = new RelayCache(100);

        private static readonly object LockObj = new object();
        private static Blockchain instance;

        private readonly NeoSystem system;
        private readonly List<UInt256> headerIndex = new List<UInt256>();
        private readonly Dictionary<UInt256, Block> blockCache = new Dictionary<UInt256, Block>();
        private readonly Dictionary<uint, LinkedList<Block>> blockCacheUnverified = new Dictionary<uint, LinkedList<Block>>();
        private readonly HashSet<IActorRef> subscriberActorRefs = new HashSet<IActorRef>();
        private readonly MemoryPool mempool = new MemoryPool(50_000);
        private readonly ConcurrentDictionary<UInt256, Transaction> unverifiedMempool = new ConcurrentDictionary<UInt256, Transaction>();

        private uint storedHeaderCount = 0;
        private Snapshot currentSnapshot;

        static Blockchain()
        {
            Blockchain.GenesisBlock.RebuildMerkleRoot();
        }

        public Blockchain(NeoSystem system, Store store)
        {
            this.system = system;
            this.Store = store;

            lock (LockObj)
            {
                if (instance != null)
                {
                    throw new InvalidOperationException();
                }

                var headersToAdd = store
                    .GetHeaderHashList()
                    .Find()
                    .OrderBy(p => (uint)p.Key)
                    .SelectMany(p => p.Value.Hashes);

                this.headerIndex.AddRange(headersToAdd);
                this.storedHeaderCount += (uint)this.headerIndex.Count;
                if (this.storedHeaderCount == 0)
                {
                    var blockHashesToAdd = store.GetBlocks()
                        .Find()
                        .OrderBy(p => p.Value.TrimmedBlock.Index)
                        .Select(p => p.Key);

                    this.headerIndex.AddRange(blockHashesToAdd);
                }
                else
                {
                    var hashIndex = store.GetHeaderHashIndex().Get();
                    if (hashIndex.Index >= this.storedHeaderCount)
                    {
                        var cache = store.GetBlocks();
                        for (var hash = hashIndex.Hash; hash != this.headerIndex[(int)this.storedHeaderCount - 1];)
                        {
                            this.headerIndex.Insert((int)this.storedHeaderCount, hash);
                            hash = cache[hash].TrimmedBlock.PrevHash;
                        }
                    }
                }

                if (headerIndex.Count == 0)
                {
                    this.Persist(GenesisBlock);
                }
                else
                {
                    this.UpdateCurrentSnapshot();
                }

                Blockchain.instance = this;
            }
        }

        public static Blockchain Instance
        {
            get
            {
                while (Blockchain.instance == null)
                {
                    Thread.Sleep(10);
                }

                return Blockchain.instance;
            }
        }

        public Store Store { get; }

        public uint Height => this.currentSnapshot.Height;

        public uint HeaderHeight => (uint)this.headerIndex.Count - 1;

        public UInt256 CurrentBlockHash => this.currentSnapshot.CurrentBlockHash;

        public UInt256 CurrentHeaderHash => this.headerIndex[this.headerIndex.Count - 1];

        public static UInt160 GetConsensusAddress(ECPoint[] validators) =>
            Contract
                .CreateMultiSigRedeemScript(
                    validators.Length - ((validators.Length - 1) / 3),
                    validators)
                .ToScriptHash();

        public static Props Props(NeoSystem system, Store store) =>
            Akka.Actor.Props
                .Create(() => new Blockchain(system, store))
                .WithMailbox("blockchain-mailbox");

        public bool ContainsBlock(UInt256 hash)
        {
            if (this.blockCache.ContainsKey(hash))
            {
                return true;
            }

            return this.Store.ContainsBlock(hash);
        }

        public bool ContainsTransaction(UInt256 hash)
        {
            if (this.mempool.ContainsKey(hash))
            {
                return true;
            }

            return this.Store.ContainsTransaction(hash);
        }

        public Block GetBlock(UInt256 hash)
        {
            if (this.blockCache.TryGetValue(hash, out Block block))
            {
                return block;
            }

            return this.Store.GetBlock(hash);
        }

        public UInt256 GetBlockHash(uint index)
        {
            if (this.headerIndex.Count <= index)
            {
                return null;
            }

            return this.headerIndex[(int)index];
        }

        public Snapshot GetSnapshot() => this.Store.GetSnapshot();

        public IEnumerable<Transaction> GetMemoryPool() => this.mempool;

        public Transaction GetTransaction(UInt256 hash)
        {
            if (this.mempool.TryGetValue(hash, out Transaction transaction))
            {
                return transaction;
            }

            return this.Store.GetTransaction(hash);
        }

        internal static void ProcessAccountStateDescriptor(StateDescriptor descriptor, Snapshot snapshot)
        {
            var hash = new UInt160(descriptor.Key);
            var accountState = snapshot.Accounts.GetAndChange(hash, () => new AccountState(hash));
            switch (descriptor.Field)
            {
                case "Votes":
                    var balance = accountState.GetBalance(GoverningToken.Hash);
                    foreach (var pubkey in accountState.Votes)
                    {
                        var validator = snapshot.Validators.GetAndChange(pubkey);
                        validator.Votes -= balance;
                        if (!validator.Registered && validator.Votes.Equals(Fixed8.Zero))
                        {
                            snapshot.Validators.Delete(pubkey);
                        }
                    }

                    var votes = descriptor.Value
                        .AsSerializableArray<ECPoint>()
                        .Distinct()
                        .ToArray();

                    if (votes.Length != accountState.Votes.Length)
                    {
                        var validatorsCountState = snapshot.ValidatorsCount.GetAndChange();
                        if (accountState.Votes.Length > 0)
                        {
                            validatorsCountState.Votes[accountState.Votes.Length - 1] -= balance;
                        }

                        if (votes.Length > 0)
                        {
                            validatorsCountState.Votes[votes.Length - 1] += balance;
                        }
                    }

                    accountState.Votes = votes;
                    foreach (var pubkey in accountState.Votes)
                    {
                        snapshot.Validators.GetAndChange(pubkey, () => new ValidatorState(pubkey)).Votes += balance;
                    }

                    break;
            }
        }

        internal static void ProcessValidatorStateDescriptor(StateDescriptor descriptor, Snapshot snapshot)
        {
            var pubkey = ECPoint.DecodePoint(descriptor.Key, ECCurve.Secp256r1);
            var validator = snapshot.Validators.GetAndChange(pubkey, () => new ValidatorState(pubkey));
            switch (descriptor.Field)
            {
                case "Registered":
                    validator.Registered = BitConverter.ToBoolean(descriptor.Value, 0);
                    break;
            }
        }

        internal Transaction GetUnverifiedTransaction(UInt256 hash)
        {
            this.unverifiedMempool.TryGetValue(hash, out Transaction transaction);
            return transaction;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Register _:
                    this.OnRegister();
                    break;
                case Import import:
                    this.OnImport(import.Blocks);
                    break;
                case Header[] headers:
                    this.OnNewHeaders(headers);
                    break;
                case Block block:
                    this.Sender.Tell(this.OnNewBlock(block));
                    break;
                case Transaction transaction:
                    var newTransactionMessage = this.OnNewTransaction(transaction);
                    this.Sender.Tell(newTransactionMessage);
                    break;
                case ConsensusPayload payload:
                    this.Sender.Tell(this.OnNewConsensus(payload));
                    break;
                case Terminated terminated:
                    this.subscriberActorRefs.Remove(terminated.ActorRef);
                    break;
            }
        }

        protected override void PostStop()
        {
            base.PostStop();
            this.currentSnapshot?.Dispose();
        }

        private void Distribute(object message)
        {
            foreach (var subscriber in this.subscriberActorRefs)
            {
                subscriber.Tell(message);
            }
        }

        private void OnImport(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block.Index <= this.Height)
                {
                    continue;
                }

                if (block.Index != this.Height + 1)
                {
                    throw new InvalidOperationException();
                }

                this.Persist(block);
                this.SaveHeaderHashList();
            }

            this.Sender.Tell(new ImportCompleted());
        }

        private void AddUnverifiedBlockToCache(Block block)
        {
            if (!this.blockCacheUnverified.TryGetValue(block.Index, out LinkedList<Block> blocks))
            {
                blocks = new LinkedList<Block>();
                this.blockCacheUnverified.Add(block.Index, blocks);
            }

            blocks.AddLast(block);
        }

        private RelayResultReason OnNewBlock(Block block)
        {
            if (block.Index <= this.Height)
            {
                return RelayResultReason.AlreadyExists;
            }

            if (this.blockCache.ContainsKey(block.Hash))
            {
                return RelayResultReason.AlreadyExists;
            }

            if (block.Index - 1 >= this.headerIndex.Count)
            {
                this.AddUnverifiedBlockToCache(block);
                return RelayResultReason.UnableToVerify;
            }

            if (block.Index == this.headerIndex.Count)
            {
                if (!block.Verify(this.currentSnapshot))
                {
                    return RelayResultReason.Invalid;
                }
            }
            else
            {
                if (!block.Hash.Equals(this.headerIndex[(int)block.Index]))
                {
                    return RelayResultReason.Invalid;
                }
            }

            if (block.Index == Height + 1)
            {
                var blockToPersist = block;
                var blocksToPersistList = new List<Block>();
                while (true)
                {
                    blocksToPersistList.Add(blockToPersist);
                    if (blockToPersist.Index + 1 >= this.headerIndex.Count)
                    {
                        break;
                    }

                    var hash = this.headerIndex[(int)blockToPersist.Index + 1];
                    if (!this.blockCache.TryGetValue(hash, out blockToPersist))
                    {
                        break;
                    }
                }

                var blocksPersisted = 0;
                foreach (var item in blocksToPersistList)
                {
                    this.blockCacheUnverified.Remove(item.Index);
                    this.Persist(item);

                    if (blocksPersisted++ < blocksToPersistList.Count - 2)
                    {
                        continue;
                    }

                    // Relay most recent 2 blocks persisted
                    if (item.Index + 100 >= this.headerIndex.Count)
                    {
                        this.system.LocalNodeActorRef.Tell(new LocalNode.RelayDirectly(item));
                    }
                }

                this.SaveHeaderHashList();

                if (this.blockCacheUnverified.TryGetValue(this.Height + 1, out LinkedList<Block> unverifiedBlocks))
                {
                    foreach (var unverifiedBlock in unverifiedBlocks)
                    {
                        this.Self.Tell(unverifiedBlock, ActorRefs.NoSender);
                    }

                    this.blockCacheUnverified.Remove(this.Height + 1);
                }
            }
            else
            {
                this.blockCache.Add(block.Hash, block);
                if (block.Index + 100 >= this.headerIndex.Count)
                {
                    var relayDirectlyMessage = new LocalNode.RelayDirectly(block);
                    this.system.LocalNodeActorRef.Tell(relayDirectlyMessage);
                }

                if (block.Index == this.headerIndex.Count)
                {
                    this.headerIndex.Add(block.Hash);
                    using (var snapshot = this.GetSnapshot())
                    {
                        var blockState = new BlockState
                        {
                            SystemFeeAmount = 0,
                            TrimmedBlock = block.Header.Trim()
                        };

                        snapshot.Blocks.Add(block.Hash, blockState);
                        snapshot.HeaderHashIndex.GetAndChange().Hash = block.Hash;
                        snapshot.HeaderHashIndex.GetAndChange().Index = block.Index;

                        this.SaveHeaderHashList(snapshot);

                        snapshot.Commit();
                    }

                    this.UpdateCurrentSnapshot();
                }
            }

            return RelayResultReason.Succeed;
        }

        private RelayResultReason OnNewConsensus(ConsensusPayload payload)
        {
            if (!payload.Verify(this.currentSnapshot))
            {
                return RelayResultReason.Invalid;
            }

            this.system.ConsensusServiceActorRef?.Tell(payload);
            this.RelayCache.Add(payload);

            var relayDirectlyMessage = new LocalNode.RelayDirectly(payload);
            this.system.LocalNodeActorRef.Tell(relayDirectlyMessage);

            return RelayResultReason.Succeed;
        }

        private void OnNewHeaders(Header[] headers)
        {
            using (var snapshot = this.GetSnapshot())
            {
                foreach (var header in headers)
                {
                    if (header.Index - 1 >= this.headerIndex.Count)
                    {
                        break;
                    }

                    if (header.Index < this.headerIndex.Count)
                    {
                        continue;
                    }

                    if (!header.Verify(snapshot))
                    {
                        break;
                    }

                    this.headerIndex.Add(header.Hash);

                    var blockState = new BlockState
                    {
                        SystemFeeAmount = 0,
                        TrimmedBlock = header.Trim()
                    };

                    snapshot.Blocks.Add(header.Hash, blockState);

                    snapshot.HeaderHashIndex.GetAndChange().Hash = header.Hash;
                    snapshot.HeaderHashIndex.GetAndChange().Index = header.Index;
                }

                this.SaveHeaderHashList(snapshot);
                snapshot.Commit();
            }

            this.UpdateCurrentSnapshot();

            var headerTaskCompletedMessage = new TaskManager.HeaderTaskCompleted();
            this.system.TaskManagerActorRef.Tell(headerTaskCompletedMessage, this.Sender);
        }

        private RelayResultReason OnNewTransaction(Transaction transaction)
        {
            if (transaction.Type == TransactionType.MinerTransaction)
            {
                return RelayResultReason.Invalid;
            }

            if (this.ContainsTransaction(transaction.Hash))
            {
                return RelayResultReason.AlreadyExists;
            }

            if (!transaction.Verify(currentSnapshot, this.GetMemoryPool()))
            {
                return RelayResultReason.Invalid;
            }

            if (!Plugin.CheckPolicy(transaction))
            {
                return RelayResultReason.PolicyFail;
            }

            if (!this.mempool.TryAdd(transaction.Hash, transaction))
            {
                return RelayResultReason.OutOfMemory;
            }

            var relayDirectlyMessage = new LocalNode.RelayDirectly(transaction);
            this.system.LocalNodeActorRef.Tell(relayDirectlyMessage);
            return RelayResultReason.Succeed;
        }

        private void OnPersistCompleted(Block block)
        {
            this.blockCache.Remove(block.Hash);
            foreach (var tx in block.Transactions)
            {
                this.mempool.TryRemove(tx.Hash, out _);
            }

            this.unverifiedMempool.Clear();

            var unverifiedTransactions = this.mempool
                .OrderByDescending(p => p.NetworkFee / p.Size)
                .ThenByDescending(p => p.NetworkFee)
                .ThenByDescending(p => new BigInteger(p.Hash.ToArray()));

            foreach (var tx in unverifiedTransactions)
            {
                this.unverifiedMempool.TryAdd(tx.Hash, tx);
                this.Self.Tell(tx, ActorRefs.NoSender);
            }

            this.mempool.Clear();

            var persistCompletedMessage = new PersistCompleted(block);
            this.system.ConsensusServiceActorRef?.Tell(persistCompletedMessage);

            this.Distribute(persistCompletedMessage);
        }

        private void OnRegister()
        {
            this.subscriberActorRefs.Add(this.Sender);
            UntypedActor.Context.Watch(this.Sender);
        }

        private void Persist(Block block)
        {
            using (var snapshot = this.GetSnapshot())
            {
                var allResults = new List<ApplicationExecuted>();

                snapshot.PersistingBlock = block;
                var transactionsFeeSum = (long)block.Transactions.Sum(p => p.SystemFee);
                var blockState = new BlockState
                {
                    SystemFeeAmount = snapshot.GetSysFeeAmount(block.PrevHash) + transactionsFeeSum,
                    TrimmedBlock = block.Trim()
                };

                snapshot.Blocks.Add(block.Hash, blockState);
                foreach (var tx in block.Transactions)
                {
                    var transactionState = new TransactionState
                    {
                        BlockIndex = block.Index,
                        Transaction = tx
                    };

                    var unspentCoinsState = new UnspentCoinState
                    {
                        Items = Enumerable.Repeat(CoinStates.Confirmed, tx.Outputs.Length).ToArray()
                    };

                    snapshot.Transactions.Add(tx.Hash, transactionState);
                    snapshot.UnspentCoins.Add(tx.Hash, unspentCoinsState);

                    foreach (var output in tx.Outputs)
                    {
                        var accountState = snapshot.Accounts.GetAndChange(output.ScriptHash, () => new AccountState(output.ScriptHash));
                        if (accountState.Balances.ContainsKey(output.AssetId))
                        {
                            accountState.Balances[output.AssetId] += output.Value;
                        }
                        else
                        {
                            accountState.Balances[output.AssetId] = output.Value;
                        }

                        if (output.AssetId.Equals(GoverningToken.Hash) && accountState.Votes.Length > 0)
                        {
                            foreach (var pubkey in accountState.Votes)
                            {
                                snapshot.Validators.GetAndChange(pubkey, () => new ValidatorState(pubkey)).Votes += output.Value;
                            }

                            snapshot.ValidatorsCount.GetAndChange().Votes[accountState.Votes.Length - 1] += output.Value;
                        }
                    }

                    foreach (var group in tx.Inputs.GroupBy(p => p.PrevHash))
                    {
                        var previousTransactionState = snapshot.Transactions[group.Key];
                        foreach (var input in group)
                        {
                            snapshot.UnspentCoins.GetAndChange(input.PrevHash).Items[input.PrevIndex] |= CoinStates.Spent;

                            var previousTransactionOutput = previousTransactionState.Transaction.Outputs[input.PrevIndex];
                            var accountState = snapshot.Accounts.GetAndChange(previousTransactionOutput.ScriptHash);

                            if (previousTransactionOutput.AssetId.Equals(GoverningToken.Hash))
                            {
                                snapshot
                                    .SpentCoins
                                    .GetAndChange(
                                        input.PrevHash,
                                        () => new SpentCoinState
                                        {
                                            TransactionHash = input.PrevHash,
                                            TransactionHeight = previousTransactionState.BlockIndex,
                                            Items = new Dictionary<ushort, uint>()
                                        })
                                    .Items
                                    .Add(input.PrevIndex, block.Index);

                                if (accountState.Votes.Length > 0)
                                {
                                    foreach (var pubkey in accountState.Votes)
                                    {
                                        var validatorState = snapshot.Validators.GetAndChange(pubkey);
                                        validatorState.Votes -= previousTransactionOutput.Value;
                                        if (!validatorState.Registered && validatorState.Votes.Equals(Fixed8.Zero))
                                        {
                                            snapshot.Validators.Delete(pubkey);
                                        }
                                    }

                                    var votes = snapshot.ValidatorsCount.GetAndChange().Votes[accountState.Votes.Length - 1];
                                    votes -= previousTransactionOutput.Value;
                                }
                            }

                            accountState.Balances[previousTransactionOutput.AssetId] -= previousTransactionOutput.Value;
                        }
                    }

                    var executionResults = new List<ApplicationExecutionResult>();
                    switch (tx)
                    {
#pragma warning disable CS0612
                        case RegisterTransaction registerTransaction:
                            {
                                var asset = new AssetState
                                {
                                    AssetId = registerTransaction.Hash,
                                    Type = registerTransaction.AssetType,
                                    Name = registerTransaction.Name,
                                    Amount = registerTransaction.Amount,
                                    Available = Fixed8.Zero,
                                    Precision = registerTransaction.Precision,
                                    Fee = Fixed8.Zero,
                                    FeeAddress = new UInt160(),
                                    Owner = registerTransaction.Owner,
                                    Admin = registerTransaction.Admin,
                                    Issuer = registerTransaction.Admin,
                                    Expiration = block.Index + (2 * 2000000),
                                    IsFrozen = false
                                };

                                snapshot.Assets.Add(tx.Hash, asset);
                            }

                            break;
#pragma warning restore CS0612
                        case IssueTransaction _:
                            {
                                var transactionResults = tx.GetTransactionResults().Where(p => p.Amount < Fixed8.Zero);
                                foreach (var result in transactionResults)
                                {
                                    snapshot.Assets.GetAndChange(result.AssetId).Available -= result.Amount;
                                }
                            }

                            break;
                        case ClaimTransaction _:
                            foreach (var coinReference in ((ClaimTransaction)tx).Claims)
                            {
                                var removeSuccess = snapshot
                                    .SpentCoins
                                    .TryGet(coinReference.PrevHash)
                                    ?.Items
                                    .Remove(coinReference.PrevIndex);

                                if (removeSuccess == true)
                                {
                                    snapshot.SpentCoins.GetAndChange(coinReference.PrevHash);
                                }
                            }

                            break;
#pragma warning disable CS0612
                        case EnrollmentTransaction enrollmentTransaction:
                            var validatorState = snapshot
                                .Validators
                                .GetAndChange(
                                    enrollmentTransaction.PublicKey,
                                    () => new ValidatorState(enrollmentTransaction.PublicKey));

                            validatorState.Registered = true;
                            break;
#pragma warning restore CS0612
                        case StateTransaction stateTransactions:
                            foreach (var descriptor in stateTransactions.Descriptors)
                            {
                                switch (descriptor.Type)
                                {
                                    case StateType.Account:
                                        ProcessAccountStateDescriptor(descriptor, snapshot);
                                        break;
                                    case StateType.Validator:
                                        ProcessValidatorStateDescriptor(descriptor, snapshot);
                                        break;
                                }
                            }

                            break;
#pragma warning disable CS0612
                        case PublishTransaction publishTransaction:
                            snapshot
                                .Contracts
                                .GetOrAdd(
                                    publishTransaction.ScriptHash,
                                    () => new ContractState
                                    {
                                        Script = publishTransaction.Script,
                                        ParameterList = publishTransaction.ParameterList,
                                        ReturnType = publishTransaction.ReturnType,
                                        ContractProperties = (ContractPropertyState)Convert.ToByte(publishTransaction.NeedStorage),
                                        Name = publishTransaction.Name,
                                        CodeVersion = publishTransaction.CodeVersion,
                                        Author = publishTransaction.Author,
                                        Email = publishTransaction.Email,
                                        Description = publishTransaction.Description
                                    });

                            break;
#pragma warning restore CS0612
                        case InvocationTransaction invocationTransaction:
                            {
                                using (var engine = new ApplicationEngine(
                                    TriggerType.Application,
                                    invocationTransaction,
                                    snapshot.Clone(),
                                    invocationTransaction.Gas))
                                {
                                    engine.LoadScript(invocationTransaction.Script);
                                    if (engine.Execute())
                                    {
                                        engine.Service.Commit();
                                    }

                                    var executionResult = new ApplicationExecutionResult
                                    {
                                        Trigger = TriggerType.Application,
                                        ScriptHash = invocationTransaction.Script.ToScriptHash(),
                                        VMState = engine.State,
                                        GasConsumed = engine.GasConsumed,
                                        Stack = engine.ResultStack.ToArray(),
                                        Notifications = engine.Service.Notifications.ToArray()
                                    };

                                    executionResults.Add(executionResult);
                                }
                            }

                            break;
                    }

                    if (executionResults.Count > 0)
                    {
                        var applicationExecuted = new ApplicationExecuted(tx, executionResults.ToArray());
                        this.Distribute(applicationExecuted);

                        allResults.Add(applicationExecuted);
                    }
                }

                snapshot.BlockHashIndex.GetAndChange().Hash = block.Hash;
                snapshot.BlockHashIndex.GetAndChange().Index = block.Index;
                if (block.Index == this.headerIndex.Count)
                {
                    this.headerIndex.Add(block.Hash);

                    snapshot.HeaderHashIndex.GetAndChange().Hash = block.Hash;
                    snapshot.HeaderHashIndex.GetAndChange().Index = block.Index;
                }

                foreach (var plugin in Plugin.PersistencePlugins)
                {
                    plugin.OnPersist(snapshot, allResults);
                }

                snapshot.Commit();
            }

            this.UpdateCurrentSnapshot();
            this.OnPersistCompleted(block);
        }

        private void SaveHeaderHashList(Snapshot snapshot = null)
        {
            if (this.headerIndex.Count - this.storedHeaderCount < 2000)
            {
                return;
            }

            var snapshotCreated = snapshot == null;
            if (snapshotCreated)
            {
                snapshot = this.GetSnapshot();
            }

            try
            {
                while (this.headerIndex.Count - this.storedHeaderCount >= 2000)
                {
                    var hashes = this.headerIndex
                        .Skip((int)this.storedHeaderCount)
                        .Take(2000)
                        .ToArray();

                    var headerHashList = new HeaderHashList
                    {
                        Hashes = hashes
                    };

                    snapshot.HeaderHashList.Add(this.storedHeaderCount, headerHashList);
                    this.storedHeaderCount += 2000;
                }

                if (snapshotCreated)
                {
                    snapshot.Commit();
                }
            }
            finally
            {
                if (snapshotCreated)
                {
                    snapshot.Dispose();
                }
            }
        }

        private void UpdateCurrentSnapshot() =>
            Interlocked.Exchange(ref this.currentSnapshot, this.GetSnapshot())?.Dispose();

        public class Register
        {
        }

        public class ApplicationExecuted
        {
            public ApplicationExecuted(Transaction transaction, ApplicationExecutionResult[] results)
            {
                this.Transaction = transaction;
                this.ExecutionResults = results;
            }

            public Transaction Transaction { get; private set; }

            public ApplicationExecutionResult[] ExecutionResults { get; private set; }
        }

        public class PersistCompleted
        {
            public PersistCompleted(Block block)
            {
                this.Block = block;
            }

            public Block Block { get; private set; }
        }

        public class Import
        {
            public Import(IEnumerable<Block> blocks)
            {
                this.Blocks = blocks;
            }

            public IEnumerable<Block> Blocks { get; private set; }
        }

        public class ImportCompleted
        {
        }
    }
}
