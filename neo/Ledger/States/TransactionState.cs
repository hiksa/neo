using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;

namespace Neo.Ledger.States
{
    public class TransactionState : StateBase, ICloneable<TransactionState>
    {
        public uint BlockIndex;
        public Transaction Transaction;

        public override int Size => base.Size + sizeof(uint) + this.Transaction.Size;

        TransactionState ICloneable<TransactionState>.Clone()
        {
            return new TransactionState
            {
                BlockIndex = this.BlockIndex,
                Transaction = this.Transaction
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.BlockIndex = reader.ReadUInt32();
            this.Transaction = Transaction.DeserializeFrom(reader);
        }

        void ICloneable<TransactionState>.FromReplica(TransactionState replica)
        {
            this.BlockIndex = replica.BlockIndex;
            this.Transaction = replica.Transaction;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(this.BlockIndex);
            writer.Write(this.Transaction);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["height"] = this.BlockIndex;
            json["tx"] = this.Transaction.ToJson();
            return json;
        }
    }
}
