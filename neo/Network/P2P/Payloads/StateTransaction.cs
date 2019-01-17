using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Persistence;
using Neo.SmartContract;

namespace Neo.Network.P2P.Payloads
{
    public class StateTransaction : Transaction
    {
        public StateTransaction()
            : base(TransactionType.StateTransaction)
        {
        }

        public StateDescriptor[] Descriptors { get; private set; }

        public override int Size => base.Size + this.Descriptors.GetVarSize();

        public override Fixed8 SystemFee => this.Descriptors.Sum(p => p.SystemFee);

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["descriptors"] = new JArray(this.Descriptors.Select(p => p.ToJson()));
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            foreach (var stateDescriptor in this.Descriptors)
            {
                if (!stateDescriptor.Verify(snapshot))
                {
                    return false;
                }
            }

            return base.Verify(snapshot, mempool);
        }
        
        public override UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var hashesForVerifying = new HashSet<UInt160>(base.GetScriptHashesForVerifying(snapshot));
            foreach (var stateDescriptor in this.Descriptors)
            {
                switch (stateDescriptor.Type)
                {
                    case StateType.Account:
                        hashesForVerifying.UnionWith(this.GetScriptHashesForVerifyingAccount(stateDescriptor));
                        break;
                    case StateType.Validator:
                        hashesForVerifying.UnionWith(this.GetScriptHashesForVerifyingValidator(stateDescriptor));
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }

            return hashesForVerifying.OrderBy(p => p).ToArray();
        }

        protected override void DeserializeExclusiveData(BinaryReader reader) =>
            this.Descriptors = reader.ReadSerializableArray<StateDescriptor>(16);

        protected override void SerializeExclusiveData(BinaryWriter writer) =>
            writer.Write(this.Descriptors);

        private IEnumerable<UInt160> GetScriptHashesForVerifyingAccount(StateDescriptor descriptor)
        {
            switch (descriptor.Field)
            {
                case "Votes":
                    yield return new UInt160(descriptor.Key);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        private IEnumerable<UInt160> GetScriptHashesForVerifyingValidator(StateDescriptor descriptor)
        {
            switch (descriptor.Field)
            {
                case "Registered":
                    var publicKey = ECPoint.DecodePoint(descriptor.Key, ECCurve.Secp256r1);
                    var validator = Contract.CreateSignatureRedeemScript(publicKey).ToScriptHash();
                    yield return validator;
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
