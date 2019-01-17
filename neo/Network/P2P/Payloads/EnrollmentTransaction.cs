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
    [Obsolete]
    public class EnrollmentTransaction : Transaction
    {
        private UInt160 scriptHash = null;

        public EnrollmentTransaction()
            : base(TransactionType.EnrollmentTransaction)
        {
        }

        public ECPoint PublicKey { get; private set; }

        public override int Size => base.Size + this.PublicKey.Size;

        internal UInt160 ScriptHash
        {
            get
            {
                if (this.scriptHash == null)
                {
                    this.scriptHash = Contract.CreateSignatureRedeemScript(this.PublicKey).ToScriptHash();
                }

                return this.scriptHash;
            }
        }

        public override UInt160[] GetScriptHashesForVerifying(Snapshot snapshot) =>
            base.GetScriptHashesForVerifying(snapshot)
                .Union(new UInt160[] { this.ScriptHash })
                .OrderBy(p => p)
                .ToArray();     

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["pubkey"] = this.PublicKey.ToString();
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            return false;
        }

        protected override void SerializeExclusiveData(BinaryWriter writer) =>
            writer.Write(this.PublicKey);

        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version != 0)
            {
                throw new FormatException();
            }

            this.PublicKey = ECPoint.DeserializeFrom(reader, ECCurve.Secp256r1);
        }
    }
}
