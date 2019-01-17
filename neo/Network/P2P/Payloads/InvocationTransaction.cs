using System;
using System.Collections.Generic;
using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Persistence;

namespace Neo.Network.P2P.Payloads
{
    public class InvocationTransaction : Transaction
    {
        public InvocationTransaction()
            : base(TransactionType.InvocationTransaction)
        {
        }

        public byte[] Script { get; set; }

        public Fixed8 Gas { get; set; }

        public override int Size => base.Size + this.Script.GetVarSize();

        public override Fixed8 SystemFee => this.Gas;

        public static Fixed8 GetGas(Fixed8 consumed)
        {
            var gas = consumed - Fixed8.FromDecimal(10);
            if (gas <= Fixed8.Zero)
            {
                return Fixed8.Zero;
            }

            return gas.Ceiling();
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["script"] = this.Script.ToHexString();
            json["gas"] = this.Gas.ToString();
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (this.Gas.GetData() % 100_000_000 != 0)
            {
                return false;
            }

            return base.Verify(snapshot, mempool);
        }
        
        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version > 1)
            {
                throw new FormatException();
            }

            this.Script = reader.ReadVarBytes(65536);
            if (this.Script.Length == 0)
            {
                throw new FormatException();
            }

            if (this.Version >= 1)
            {
                this.Gas = reader.ReadSerializable<Fixed8>();
                if (this.Gas < Fixed8.Zero)
                {
                    throw new FormatException();
                }
            }
            else
            {
                this.Gas = Fixed8.Zero;
            }
        }

        protected override void SerializeExclusiveData(BinaryWriter writer)
        {
            writer.WriteVarBytes(this.Script);
            if (this.Version >= 1)
            {
                writer.Write(this.Gas);
            }
        }
    }
}
