using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using VMArray = Neo.VM.Types.Array;
using VMBoolean = Neo.VM.Types.Boolean;

namespace Neo.SmartContract
{
    public class StandardService : IDisposable, IInteropService
    {
        protected readonly TriggerType Trigger;
        protected readonly Snapshot Snapshot;
        protected readonly List<IDisposable> Disposables = new List<IDisposable>();
        protected readonly Dictionary<UInt160, UInt160> ContractsCreated = new Dictionary<UInt160, UInt160>();

        private readonly List<NotifyEventArgs> notifications = new List<NotifyEventArgs>();
        private readonly Dictionary<uint, Func<ExecutionEngine, bool>> methods = new Dictionary<uint, Func<ExecutionEngine, bool>>();
        private readonly Dictionary<uint, long> prices = new Dictionary<uint, long>();

        public StandardService(TriggerType trigger, Snapshot snapshot)
        {
            this.Trigger = trigger;
            this.Snapshot = snapshot;
            this.Register("System.ExecutionEngine.GetScriptContainer", this.ExecutionEngine_GetScriptContainer, 1);
            this.Register("System.ExecutionEngine.GetExecutingScriptHash", this.ExecutionEngine_GetExecutingScriptHash, 1);
            this.Register("System.ExecutionEngine.GetCallingScriptHash", this.ExecutionEngine_GetCallingScriptHash, 1);
            this.Register("System.ExecutionEngine.GetEntryScriptHash", this.ExecutionEngine_GetEntryScriptHash, 1);
            this.Register("System.Runtime.Platform", this.Runtime_Platform, 1);
            this.Register("System.Runtime.GetTrigger", this.Runtime_GetTrigger, 1);
            this.Register("System.Runtime.CheckWitness", this.Runtime_CheckWitness, 200);
            this.Register("System.Runtime.Notify", this.Runtime_Notify, 1);
            this.Register("System.Runtime.Log", this.Runtime_Log, 1);
            this.Register("System.Runtime.GetTime", this.Runtime_GetTime, 1);
            this.Register("System.Runtime.Serialize", this.Runtime_Serialize, 1);
            this.Register("System.Runtime.Deserialize", this.Runtime_Deserialize, 1);
            this.Register("System.Blockchain.GetHeight", this.Blockchain_GetHeight, 1);
            this.Register("System.Blockchain.GetHeader", this.Blockchain_GetHeader, 100);
            this.Register("System.Blockchain.GetBlock", this.Blockchain_GetBlock, 200);
            this.Register("System.Blockchain.GetTransaction", this.Blockchain_GetTransaction, 200);
            this.Register("System.Blockchain.GetTransactionHeight", this.Blockchain_GetTransactionHeight, 100);
            this.Register("System.Blockchain.GetContract", this.Blockchain_GetContract, 100);
            this.Register("System.Header.GetIndex", this.Header_GetIndex, 1);
            this.Register("System.Header.GetHash", this.Header_GetHash, 1);
            this.Register("System.Header.GetPrevHash", this.Header_GetPrevHash, 1);
            this.Register("System.Header.GetTimestamp", this.Header_GetTimestamp, 1);
            this.Register("System.Block.GetTransactionCount", this.Block_GetTransactionCount, 1);
            this.Register("System.Block.GetTransactions", this.Block_GetTransactions, 1);
            this.Register("System.Block.GetTransaction", this.Block_GetTransaction, 1);
            this.Register("System.Transaction.GetHash", this.Transaction_GetHash, 1);
            this.Register("System.Contract.Destroy", this.Contract_Destroy, 1);
            this.Register("System.Contract.GetStorageContext", this.Contract_GetStorageContext, 1);
            this.Register("System.Storage.GetContext", this.Storage_GetContext, 1);
            this.Register("System.Storage.GetReadOnlyContext", this.Storage_GetReadOnlyContext, 1);
            this.Register("System.Storage.Get", this.Storage_Get, 100);
            this.Register("System.Storage.Put", this.Storage_Put);
            this.Register("System.Storage.PutEx", this.Storage_PutEx);
            this.Register("System.Storage.Delete", this.Storage_Delete, 100);
            this.Register("System.StorageContext.AsReadOnly", this.StorageContext_AsReadOnly, 1);
        }

        public static event EventHandler<NotifyEventArgs> Notify;

        public static event EventHandler<LogEventArgs> Log;

        public IReadOnlyList<NotifyEventArgs> Notifications => this.notifications;

        public void Commit() => this.Snapshot.Commit();

        public void Dispose()
        {
            foreach (IDisposable disposable in this.Disposables)
            {
                disposable.Dispose();
            }

            this.Disposables.Clear();
        }

        public long GetPrice(uint hash)
        {
            this.prices.TryGetValue(hash, out long price);
            return price;
        }

        bool IInteropService.Invoke(byte[] method, ExecutionEngine engine)
        {
            var hash = method.Length == 4
                ? BitConverter.ToUInt32(method, 0)
                : Encoding.ASCII.GetString(method).ToInteropMethodHash();

            if (!this.methods.TryGetValue(hash, out Func<ExecutionEngine, bool> func))
            {
                return false;
            }

            return func(engine);
        }

        internal bool CheckStorageContext(StorageContext context)
        {
            var contract = this.Snapshot.Contracts.TryGet(context.ScriptHash);
            return contract != null && contract.HasStorage;
        }

        protected void Register(string method, Func<ExecutionEngine, bool> handler) =>
            this.methods.Add(method.ToInteropMethodHash(), handler);

        protected void Register(string method, Func<ExecutionEngine, bool> handler, long price)
        {
            this.Register(method, handler);
            this.prices.Add(method.ToInteropMethodHash(), price);
        }

        protected bool ExecutionEngine_GetScriptContainer(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(engine.ScriptContainer));
            return true;
        }

        protected bool ExecutionEngine_GetExecutingScriptHash(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(engine.CurrentContext.ScriptHash);
            return true;
        }

        protected bool ExecutionEngine_GetCallingScriptHash(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(engine.CallingContext.ScriptHash);
            return true;
        }

        protected bool ExecutionEngine_GetEntryScriptHash(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(engine.EntryContext.ScriptHash);
            return true;
        }

        protected bool Runtime_Platform(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(Encoding.ASCII.GetBytes("NEO"));
            return true;
        }

        protected bool Runtime_GetTrigger(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push((int)this.Trigger);
            return true;
        }

        protected bool CheckWitness(ExecutionEngine engine, UInt160 hash)
        {
            var container = (IVerifiable)engine.ScriptContainer;
            var hashesForVerifying = container.GetScriptHashesForVerifying(this.Snapshot);
            return hashesForVerifying.Contains(hash);
        }

        protected bool CheckWitness(ExecutionEngine engine, ECPoint pubkey) =>
            this.CheckWitness(engine, Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash());

        protected bool Runtime_CheckWitness(ExecutionEngine engine)
        {
            var hashOrPubkey = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            bool result;
            if (hashOrPubkey.Length == 20)
            {
                result = this.CheckWitness(engine, new UInt160(hashOrPubkey));
            }
            else if (hashOrPubkey.Length == 33)
            {
                result = this.CheckWitness(engine, ECPoint.DecodePoint(hashOrPubkey, ECCurve.Secp256r1));
            }
            else
            {
                return false;
            }

            engine.CurrentContext.EvaluationStack.Push(result);
            return true;
        }

        protected bool Runtime_Notify(ExecutionEngine engine)
        {
            var state = engine.CurrentContext.EvaluationStack.Pop();
            var currentContextHash = new UInt160(engine.CurrentContext.ScriptHash);
            var notification = new NotifyEventArgs(engine.ScriptContainer, currentContextHash, state);

            StandardService.Notify?.Invoke(this, notification);

            this.notifications.Add(notification);
            return true;
        }

        protected bool Runtime_Log(ExecutionEngine engine)
        {
            var rawMessage = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var message = Encoding.UTF8.GetString(rawMessage);
            var currentContextHash = new UInt160(engine.CurrentContext.ScriptHash);
            var logEventArgs = new LogEventArgs(engine.ScriptContainer, currentContextHash, message);

            Log?.Invoke(this, logEventArgs);
            return true;
        }

        protected bool Runtime_GetTime(ExecutionEngine engine)
        {
            if (this.Snapshot.PersistingBlock == null)
            {
                var header = this.Snapshot.GetHeader(this.Snapshot.CurrentBlockHash);
                engine.CurrentContext.EvaluationStack.Push(header.Timestamp + Blockchain.SecondsPerBlock);
            }
            else
            {
                engine.CurrentContext.EvaluationStack.Push(this.Snapshot.PersistingBlock.Timestamp);
            }

            return true;
        }

        protected bool Runtime_Serialize(ExecutionEngine engine)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                try
                {
                    this.SerializeStackItem(engine.CurrentContext.EvaluationStack.Pop(), writer);
                }
                catch (NotSupportedException)
                {
                    return false;
                }

                writer.Flush();
                if (ms.Length > ApplicationEngine.MaxItemSize)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(ms.ToArray());
            }

            return true;
        }

        protected bool Runtime_Deserialize(ExecutionEngine engine)
        {
            var data = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            using (var ms = new MemoryStream(data, false))
            using (var reader = new BinaryReader(ms))
            {
                StackItem item;
                try
                {
                    item = this.DeserializeStackItem(reader);
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(item);
            }

            return true;
        }

        protected bool Blockchain_GetHeight(ExecutionEngine engine)
        {
            engine.CurrentContext.EvaluationStack.Push(this.Snapshot.Height);
            return true;
        }

        protected bool Blockchain_GetHeader(ExecutionEngine engine)
        {
            var data = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            UInt256 hash;
            if (data.Length <= 5)
            {
                hash = Blockchain.Instance.GetBlockHash((uint)new BigInteger(data));
            }
            else if (data.Length == 32)
            {
                hash = new UInt256(data);
            }
            else
            {
                return false;
            }

            if (hash == null)
            {
                engine.CurrentContext.EvaluationStack.Push(new byte[0]);
            }
            else
            {
                var header = this.Snapshot.GetHeader(hash);
                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(header));
            }

            return true;
        }

        protected bool Blockchain_GetBlock(ExecutionEngine engine)
        {
            var data = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            UInt256 hash;
            if (data.Length <= 5)
            {
                hash = Blockchain.Instance.GetBlockHash((uint)new BigInteger(data));
            }
            else if (data.Length == 32)
            {
                hash = new UInt256(data);
            }
            else
            {
                return false;
            }

            if (hash == null)
            {
                engine.CurrentContext.EvaluationStack.Push(new byte[0]);
            }
            else
            {
                var block = this.Snapshot.GetBlock(hash);
                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(block));
            }

            return true;
        }

        protected bool Blockchain_GetTransaction(ExecutionEngine engine)
        {
            var hash = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var tx = this.Snapshot.GetTransaction(new UInt256(hash));

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(tx));
            return true;
        }

        protected bool Blockchain_GetTransactionHeight(ExecutionEngine engine)
        {
            var hash = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var height = (int?)this.Snapshot.Transactions.TryGet(new UInt256(hash))?.BlockIndex;

            engine.CurrentContext.EvaluationStack.Push(height ?? -1);
            return true;
        }

        protected bool Blockchain_GetContract(ExecutionEngine engine)
        {
            var hash = new UInt160(engine.CurrentContext.EvaluationStack.Pop().GetByteArray());
            var contractState = this.Snapshot.Contracts.TryGet(hash);
            if (contractState == null)
            {
                engine.CurrentContext.EvaluationStack.Push(new byte[0]);
            }
            else
            {
                var cotractStateAsStackItem = StackItem.FromInterface(contractState);
                engine.CurrentContext.EvaluationStack.Push(cotractStateAsStackItem);
            }

            return true;
        }

        protected bool Header_GetIndex(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.Index);
                return true;
            }

            return false;
        }

        protected bool Header_GetHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.Hash.ToArray());
                return true;
            }

            return false;
        }

        protected bool Header_GetPrevHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.PrevHash.ToArray());
                return true;
            }

            return false;
        }

        protected bool Header_GetTimestamp(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.Timestamp);
                return true;
            }

            return false;
        }

        protected bool Block_GetTransactionCount(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var block = interopInterface.GetInterface<Block>();
                if (block == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(block.Transactions.Length);
                return true;
            }

            return false;
        }

        protected bool Block_GetTransactions(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var block = interopInterface.GetInterface<Block>();
                if (block == null)
                {
                    return false;
                }

                if (block.Transactions.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(block.Transactions.Select(p => StackItem.FromInterface(p)).ToArray());
                return true;
            }

            return false;
        }

        protected bool Block_GetTransaction(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var block = interopInterface.GetInterface<Block>();
                var index = (int)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
                if (block == null)
                {
                    return false;
                }

                if (index < 0 || index >= block.Transactions.Length)
                {
                    return false;
                }

                var tx = block.Transactions[index];
                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(tx));
                return true;
            }

            return false;
        }

        protected bool Transaction_GetHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var tx = interopInterface.GetInterface<Transaction>();
                if (tx == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(tx.Hash.ToArray());
                return true;
            }

            return false;
        }

        protected bool Storage_GetContext(ExecutionEngine engine)
        {
            var storageContext = new StorageContext
            {
                ScriptHash = new UInt160(engine.CurrentContext.ScriptHash),
                IsReadOnly = false
            };

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(storageContext));

            return true;
        }

        protected bool Storage_GetReadOnlyContext(ExecutionEngine engine)
        {
            var storageContext = new StorageContext
            {
                ScriptHash = new UInt160(engine.CurrentContext.ScriptHash),
                IsReadOnly = true
            };

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(storageContext));

            return true;
        }

        protected bool Storage_Get(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var storageContext = interopInterface.GetInterface<StorageContext>();
                if (!this.CheckStorageContext(storageContext))
                {
                    return false;
                }

                var key = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
                var storageItem = this.Snapshot.Storages.TryGet(new StorageKey
                {
                    ScriptHash = storageContext.ScriptHash,
                    Key = key
                });

                engine.CurrentContext.EvaluationStack.Push(storageItem?.Value ?? new byte[0]);
                return true;
            }

            return false;
        }

        protected bool StorageContext_AsReadOnly(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var context = interopInterface.GetInterface<StorageContext>();
                if (!context.IsReadOnly)
                {
                    context = new StorageContext
                    {
                        ScriptHash = context.ScriptHash,
                        IsReadOnly = true
                    };
                }

                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(context));
                return true;
            }

            return false;
        }

        protected bool Contract_GetStorageContext(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var contract = interopInterface.GetInterface<ContractState>();
                if (!this.ContractsCreated.TryGetValue(contract.ScriptHash, out UInt160 created))
                {
                    return false;
                }

                if (!created.Equals(new UInt160(engine.CurrentContext.ScriptHash)))
                {
                    return false;
                }

                var storageContext = new StorageContext
                {
                    ScriptHash = contract.ScriptHash,
                    IsReadOnly = false
                };

                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(storageContext));

                return true;
            }

            return false;
        }

        protected bool Contract_Destroy(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application)
            {
                return false;
            }

            var hash = new UInt160(engine.CurrentContext.ScriptHash);
            var contract = this.Snapshot.Contracts.TryGet(hash);
            if (contract == null)
            {
                return true;
            }

            this.Snapshot.Contracts.Delete(hash);
            if (contract.HasStorage)
            {
                foreach (var pair in this.Snapshot.Storages.Find(hash.ToArray()))
                {
                    this.Snapshot.Storages.Delete(pair.Key);
                }
            }

            return true;
        }

        protected bool Storage_Put(ExecutionEngine engine)
        {
            if (!(engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface))
            {
                return false;
            }

            var context = interopInterface.GetInterface<StorageContext>();
            var key = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var value = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            return this.PutEx(context, key, value, StorageFlags.None);
        }

        protected bool Storage_PutEx(ExecutionEngine engine)
        {
            if (!(engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface))
            {
                return false;
            }

            var context = interopInterface.GetInterface<StorageContext>();
            var key = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var value = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var flags = (StorageFlags)(byte)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
            return this.PutEx(context, key, value, flags);
        }

        protected bool Storage_Delete(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application && this.Trigger != TriggerType.ApplicationR)
            {
                return false;
            }

            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var context = interopInterface.GetInterface<StorageContext>();
                if (context.IsReadOnly || !this.CheckStorageContext(context))
                {
                    return false;
                }

                var storageKey = new StorageKey
                {
                    ScriptHash = context.ScriptHash,
                    Key = engine.CurrentContext.EvaluationStack.Pop().GetByteArray()
                };

                if (this.Snapshot.Storages.TryGet(storageKey)?.IsConstant == true)
                {
                    return false;
                }

                this.Snapshot.Storages.Delete(storageKey);
                return true;
            }

            return false;
        }

        private StackItem DeserializeStackItem(BinaryReader reader)
        {
            var deserialized = new Stack<StackItem>();
            var undeserialized = 1;
            while (undeserialized-- > 0)
            {
                var stackItemType = (StackItemType)reader.ReadByte();
                switch (stackItemType)
                {
                    case StackItemType.ByteArray:
                        deserialized.Push(new ByteArray(reader.ReadVarBytes()));
                        break;
                    case StackItemType.Boolean:
                        deserialized.Push(new VMBoolean(reader.ReadBoolean()));
                        break;
                    case StackItemType.Integer:
                        deserialized.Push(new Integer(new BigInteger(reader.ReadVarBytes())));
                        break;
                    case StackItemType.Array:
                    case StackItemType.Struct:
                        {
                            var count = (int)reader.ReadVarInt(ApplicationEngine.MaxArraySize);
                            deserialized.Push(new ContainerPlaceholder
                            {
                                Type = stackItemType,
                                ElementCount = count
                            });

                            undeserialized += count;
                        }

                        break;
                    case StackItemType.Map:
                        {
                            var count = (int)reader.ReadVarInt(ApplicationEngine.MaxArraySize);
                            deserialized.Push(new ContainerPlaceholder
                            {
                                Type = stackItemType,
                                ElementCount = count
                            });

                            undeserialized += count * 2;
                        }

                        break;
                    default:
                        throw new FormatException();
                }
            }

            var tempStack = new Stack<StackItem>();
            while (deserialized.Any())
            {
                var item = deserialized.Pop();
                if (item is ContainerPlaceholder placeholder)
                {
                    switch (placeholder.Type)
                    {
                        case StackItemType.Array:
                            var array = new VMArray();
                            for (int i = 0; i < placeholder.ElementCount; i++)
                            {
                                array.Add(tempStack.Pop());
                            }

                            item = array;
                            break;
                        case StackItemType.Struct:
                            var @struct = new Struct();
                            for (int i = 0; i < placeholder.ElementCount; i++)
                            {
                                @struct.Add(tempStack.Pop());
                            }

                            item = @struct;
                            break;
                        case StackItemType.Map:
                            var map = new Map();
                            for (int i = 0; i < placeholder.ElementCount; i++)
                            {
                                var key = tempStack.Pop();
                                var value = tempStack.Pop();
                                map.Add(key, value);
                            }

                            item = map;
                            break;
                    }
                }

                tempStack.Push(item);
            }

            return tempStack.Peek();
        }

        private void SerializeStackItem(StackItem item, BinaryWriter writer)
        {
            var serialized = new List<StackItem>();
            var unserialized = new Stack<StackItem>();
            unserialized.Push(item);

            while (unserialized.Count > 0)
            {
                item = unserialized.Pop();
                switch (item)
                {
                    case ByteArray _:
                        writer.Write((byte)StackItemType.ByteArray);
                        writer.WriteVarBytes(item.GetByteArray());
                        break;
                    case VMBoolean _:
                        writer.Write((byte)StackItemType.Boolean);
                        writer.Write(item.GetBoolean());
                        break;
                    case Integer _:
                        writer.Write((byte)StackItemType.Integer);
                        writer.WriteVarBytes(item.GetByteArray());
                        break;
                    case InteropInterface _:
                        throw new NotSupportedException();
                    case VMArray array:
                        if (serialized.Any(p => object.ReferenceEquals(p, array)))
                        {
                            throw new NotSupportedException();
                        }

                        serialized.Add(array);
                        if (array is Struct)
                        {
                            writer.Write((byte)StackItemType.Struct);
                        }
                        else
                        {
                            writer.Write((byte)StackItemType.Array);
                        }

                        writer.WriteVarInt(array.Count);
                        for (int i = array.Count - 1; i >= 0; i--)
                        {
                            unserialized.Push(array[i]);
                        }

                        break;
                    case Map map:
                        if (serialized.Any(p => object.ReferenceEquals(p, map)))
                        {
                            throw new NotSupportedException();
                        }

                        serialized.Add(map);
                        writer.Write((byte)StackItemType.Map);
                        writer.WriteVarInt(map.Count);
                        foreach (var pair in map.Reverse())
                        {
                            unserialized.Push(pair.Value);
                            unserialized.Push(pair.Key);
                        }

                        break;
                }
            }
        }

        private bool PutEx(StorageContext context, byte[] key, byte[] value, StorageFlags flags)
        {
            if (this.Trigger != TriggerType.Application && this.Trigger != TriggerType.ApplicationR)
            {
                return false;
            }

            if (key.Length > 1024 || context.IsReadOnly || !this.CheckStorageContext(context))
            {
                return false;
            }

            var storageKey = new StorageKey
            {
                ScriptHash = context.ScriptHash,
                Key = key
            };

            var storageItem = this.Snapshot.Storages.GetAndChange(storageKey, () => new StorageItem());
            if (storageItem.IsConstant)
            {
                return false;
            }

            storageItem.Value = value;
            storageItem.IsConstant = flags.HasFlag(StorageFlags.Constant);
            return true;
        }
    }
}
