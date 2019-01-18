using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Persistence;
using Neo.VM;

namespace Neo.Network.P2P.Payloads
{
    public abstract class Transaction : IEquatable<Transaction>, IInventory
    {
        public const int MaxTransactionSize = 102400;

        /// <summary>
        /// Maximum number of attributes that can be contained within a transaction
        /// </summary>
        private const int MaxTransactionAttributes = 16;

        /// <summary>
        /// Reflection cache for TransactionType
        /// </summary>
        private static ReflectionCache<byte> reflectionCache = ReflectionCache<byte>.CreateFromEnum<TransactionType>();

        private UInt256 hash = null;

        private IReadOnlyDictionary<CoinReference, TransactionOutput> references;

        private Fixed8 feePerByte = -Fixed8.Satoshi;

        private Fixed8 networkFee = -Fixed8.Satoshi;

        protected Transaction(TransactionType type)
        {
            this.Type = type;
        }

        public TransactionType Type { get; private set; }

        public byte Version { get; set; }

        public TransactionAttribute[] Attributes { get; set; }

        public CoinReference[] Inputs { get; set; }

        public TransactionOutput[] Outputs { get; set; }

        public Witness[] Witnesses { get; set; }

        /// <summary>
        /// The <c>NetworkFee</c> for the transaction divided by its <c>Size</c>.
        /// <para>Note that this property must be used with care. Getting the value of this property multiple times will return the same result. The value of this property can only be obtained after the transaction has been completely built (no longer modified).</para>
        /// </summary>
        public Fixed8 FeePerByte
        {
            get
            {
                if (this.feePerByte == -Fixed8.Satoshi)
                {
                    this.feePerByte = this.NetworkFee / this.Size;
                }

                return this.feePerByte;
            }
        }

        public UInt256 Hash
        {
            get
            {
                if (this.hash == null)
                {
                    var hashData = Crypto.Default.Hash256(this.GetHashData());
                    this.hash = new UInt256(hashData);
                }

                return this.hash;
            }
        }

        InventoryType IInventory.InventoryType => InventoryType.TX;

        public bool IsLowPriority => this.NetworkFee < ProtocolSettings.Default.LowPriorityThreshold;

        public virtual Fixed8 NetworkFee
        {
            get
            {
                if (this.networkFee == -Fixed8.Satoshi)
                {
                    var input = this.References.Values
                        .Where(p => p.AssetId.Equals(Blockchain.UtilityToken.Hash))
                        .Sum(p => p.Value);

                    var output = this.Outputs
                        .Where(p => p.AssetId.Equals(Blockchain.UtilityToken.Hash))
                        .Sum(p => p.Value);

                    this.networkFee = input - output - this.SystemFee;
                }

                return this.networkFee;
            }
        }

        public IReadOnlyDictionary<CoinReference, TransactionOutput> References
        {
            get
            {
                if (this.references == null)
                {
                    var txOutputsByCointReferences = new Dictionary<CoinReference, TransactionOutput>();
                    var inputGroups = this.Inputs.GroupBy(p => p.PrevHash);
                    foreach (var inputGroup in inputGroups)
                    {
                        var tx = Blockchain.Instance.Store.GetTransaction(inputGroup.Key);
                        if (tx == null)
                        {
                            return null;
                        }

                        var references = inputGroup.Select(p => new { Input = p, Output = tx.Outputs[p.PrevIndex] });
                        foreach (var reference in references)
                        {
                            txOutputsByCointReferences.Add(reference.Input, reference.Output);
                        }
                    }

                    this.references = txOutputsByCointReferences;
                }

                return this.references;
            }
        }

        public virtual int Size => 
            sizeof(TransactionType) + sizeof(byte) 
            + this.Attributes.GetVarSize() + this.Inputs.GetVarSize() 
            + this.Outputs.GetVarSize() + this.Witnesses.GetVarSize();

        public virtual Fixed8 SystemFee => 
            ProtocolSettings.Default.SystemFee.TryGetValue(this.Type, out Fixed8 fee) 
                ? fee 
                : Fixed8.Zero;

        public static Transaction DeserializeFrom(byte[] value, int offset = 0)
        {
            using (var ms = new MemoryStream(value, offset, value.Length - offset, false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return DeserializeFrom(reader);
            }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            ((IVerifiable)this).DeserializeUnsigned(reader);

            this.Witnesses = reader.ReadSerializableArray<Witness>();
            this.OnDeserialized();
        }

        void IVerifiable.DeserializeUnsigned(BinaryReader reader)
        {
            if ((TransactionType)reader.ReadByte() != this.Type)
            {
                throw new FormatException();
            }

            this.DeserializeUnsignedWithoutType(reader);
        }

        public bool Equals(Transaction other)
        {
            if (other is null)
            {
                return false;
            }

            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj) => this.Equals(obj as Transaction);

        public override int GetHashCode() => this.Hash.GetHashCode();

        byte[] IScriptContainer.GetMessage() => this.GetHashData();

        public virtual UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            if (this.References == null)
            {
                throw new InvalidOperationException();
            }

            var hashes = new HashSet<UInt160>(this.Inputs.Select(p => this.References[p].ScriptHash));
            var hashesFromAttributes = this.Attributes
                .Where(p => p.Usage == TransactionAttributeUsage.Script)
                .Select(p => new UInt160(p.Data));

            hashes.UnionWith(hashesFromAttributes);
            foreach (var group in this.Outputs.GroupBy(p => p.AssetId))
            {
                var assetState = snapshot.Assets.TryGet(group.Key);
                if (assetState == null)
                {
                    throw new InvalidOperationException();
                }

                if (assetState.Type.HasFlag(AssetType.DutyFlag))
                {
                    hashes.UnionWith(group.Select(p => p.ScriptHash));
                }
            }

            return hashes.OrderBy(p => p).ToArray();
        }

        public IEnumerable<TransactionResult> GetTransactionResults()
        {
            if (this.References == null)
            {
                return null;
            }

            var results = this.References.Values
                .Select(p => new { p.AssetId, p.Value })
                .Concat(this.Outputs.Select(p => new { p.AssetId, Value = -p.Value }))
                .GroupBy(p => p.AssetId, (k, g) => new TransactionResult { AssetId = k, Amount = g.Sum(p => p.Value) })
                .Where(p => p.Amount != Fixed8.Zero);

            return results;
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            ((IVerifiable)this).SerializeUnsigned(writer);
            writer.Write(this.Witnesses);
        }

        void IVerifiable.SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write((byte)this.Type);
            writer.Write(this.Version);

            this.SerializeExclusiveData(writer);

            writer.Write(this.Attributes);
            writer.Write(this.Inputs);
            writer.Write(this.Outputs);
        }

        public virtual JObject ToJson()
        {
            var json = new JObject();
            json["txid"] = this.Hash.ToString();
            json["size"] = this.Size;
            json["type"] = this.Type;
            json["version"] = this.Version;
            json["attributes"] = this.Attributes.Select(p => p.ToJson()).ToArray();
            json["vin"] = this.Inputs.Select(p => p.ToJson()).ToArray();
            json["vout"] = this.Outputs.Select((p, i) => p.ToJson((ushort)i)).ToArray();
            json["sys_fee"] = this.SystemFee.ToString();
            json["net_fee"] = this.NetworkFee.ToString();
            json["scripts"] = this.Witnesses.Select(p => p.ToJson()).ToArray();
            return json;
        }

        bool IInventory.Verify(Snapshot snapshot) =>
            this.Verify(snapshot, Enumerable.Empty<Transaction>());

        public virtual bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (this.Size > Transaction.MaxTransactionSize)
            {
                return false;
            }

            for (var i = 1; i < this.Inputs.Length; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (this.Inputs[i].PrevHash == this.Inputs[j].PrevHash 
                        && this.Inputs[i].PrevIndex == this.Inputs[j].PrevIndex)
                    {
                        return false;
                    }
                }
            }

            var allInputs = mempool
                .Where(p => p != this)
                .SelectMany(p => p.Inputs)
                .Intersect(this.Inputs);

            if (allInputs.Any())
            {
                return false;
            }

            if (snapshot.IsDoubleSpend(this))
            {
                return false;
            }

            var outputGroups = this.Outputs.GroupBy(p => p.AssetId);
            foreach (var group in outputGroups)
            {
                var assetState = snapshot.Assets.TryGet(group.Key);
                if (assetState == null)
                {
                    return false;
                }

                if (assetState.Expiration <= snapshot.Height + 1 
                    && assetState.Type != AssetType.GoverningToken 
                    && assetState.Type != AssetType.UtilityToken)
                {
                    return false;
                }

                foreach (var output in group)
                {
                    if (output.Value.GetData() % (long)Math.Pow(10, 8 - assetState.Precision) != 0)
                    {
                        return false;
                    }
                }
            }

            var transactionResults = this.GetTransactionResults()?.ToArray();
            if (transactionResults == null)
            {
                return false;
            }

            var resultDestroy = transactionResults.Where(p => p.Amount > Fixed8.Zero).ToArray();
            if (resultDestroy.Length > 1)
            {
                return false;
            }

            if (resultDestroy.Length == 1 && resultDestroy[0].AssetId != Blockchain.UtilityToken.Hash)
            {
                return false;
            }

            if (this.SystemFee > Fixed8.Zero 
                && (resultDestroy.Length == 0 || resultDestroy[0].Amount < this.SystemFee))
            {
                return false;
            }

            var resultsIssue = transactionResults.Where(p => p.Amount < Fixed8.Zero).ToArray();
            switch (this.Type)
            {
                case TransactionType.MinerTransaction:
                case TransactionType.ClaimTransaction:
                    if (resultsIssue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                    {
                        return false;
                    }

                    break;
                case TransactionType.IssueTransaction:
                    if (resultsIssue.Any(p => p.AssetId == Blockchain.UtilityToken.Hash))
                    {
                        return false;
                    }

                    break;
                default:
                    if (resultsIssue.Length > 0)
                    {
                        return false;
                    }

                    break;
            }

            var ecdhAttributesCount = this.Attributes
                .Count(p => p.Usage == TransactionAttributeUsage.ECDH02 || p.Usage == TransactionAttributeUsage.ECDH03);

            if (ecdhAttributesCount > 1)
            {
                return false;
            }

            if (!this.VerifyReceivingScripts())
            {
                return false;
            }

            return this.VerifyWitnesses(snapshot);
        }

        internal static Transaction DeserializeFrom(BinaryReader reader)
        {
            var transaction = Transaction.reflectionCache.CreateInstance<Transaction>(reader.ReadByte());
            if (transaction == null)
            {
                throw new FormatException();
            }

            transaction.DeserializeUnsignedWithoutType(reader);
            transaction.Witnesses = reader.ReadSerializableArray<Witness>();
            transaction.OnDeserialized();

            return transaction;
        }

        protected virtual void DeserializeExclusiveData(BinaryReader reader)
        {
        }

        protected virtual void SerializeExclusiveData(BinaryWriter writer)
        {
        }

        protected virtual void OnDeserialized()
        {
        }

        private bool VerifyReceivingScripts()
        {
            // TODO: run ApplicationEngine
            ////foreach (UInt160 hash in Outputs.Select(p => p.ScriptHash).Distinct())
            ////{
            ////    ContractState contract = Blockchain.Default.GetContract(hash);
            ////    if (contract == null) continue;
            ////    if (!contract.Payable) return false;
            ////    using (StateReader service = new StateReader())
            ////    {
            ////        ApplicationEngine engine = new ApplicationEngine(TriggerType.VerificationR, this, Blockchain.Default, service, Fixed8.Zero);
            ////        engine.LoadScript(contract.Script, false);
            ////        using (ScriptBuilder sb = new ScriptBuilder())
            ////        {
            ////            sb.EmitPush(0);
            ////            sb.Emit(OpCode.PACK);
            ////            sb.EmitPush("receiving");
            ////            engine.LoadScript(sb.ToArray(), false);
            ////        }
            ////        if (!engine.Execute()) return false;
            ////        if (engine.EvaluationStack.Count != 1 || !engine.EvaluationStack.Pop().GetBoolean()) return false;
            ////    }
            ////}

            return true;
        }

        private void DeserializeUnsignedWithoutType(BinaryReader reader)
        {
            this.Version = reader.ReadByte();
            this.DeserializeExclusiveData(reader);
            this.Attributes = reader.ReadSerializableArray<TransactionAttribute>(Transaction.MaxTransactionAttributes);
            this.Inputs = reader.ReadSerializableArray<CoinReference>();
            this.Outputs = reader.ReadSerializableArray<TransactionOutput>(ushort.MaxValue + 1);
        }
    }
}
