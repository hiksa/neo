using System;
using System.IO;
using System.Linq;
using Neo.IO.Json;
using Neo.Ledger;

namespace Neo.Network.P2P.Payloads
{
    public class MinerTransaction : Transaction
    {
        public MinerTransaction()
            : base(TransactionType.MinerTransaction)
        {
        }

        public uint Nonce { get; set; }

        public override Fixed8 NetworkFee => Fixed8.Zero;

        public override int Size => base.Size + sizeof(uint);

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["nonce"] = this.Nonce;
            return json;
        }

        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version != 0)
            {
                throw new FormatException();
            }

            this.Nonce = reader.ReadUInt32();
        }

        protected override void OnDeserialized()
        {
            base.OnDeserialized();

            if (this.Inputs.Length != 0)
            {
                throw new FormatException();
            }

            if (this.Outputs.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
            {
                throw new FormatException();
            }
        }

        protected override void SerializeExclusiveData(BinaryWriter writer) => writer.Write(this.Nonce);
    }
}
