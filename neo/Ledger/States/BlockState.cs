using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;

namespace Neo.Ledger.States
{
    public class BlockState : StateBase, ICloneable<BlockState>
    {
        public long SystemFeeAmount;
        public TrimmedBlock TrimmedBlock;

        public override int Size => base.Size + sizeof(long) + this.TrimmedBlock.Size;

        BlockState ICloneable<BlockState>.Clone() =>
            new BlockState
            {
                SystemFeeAmount = this.SystemFeeAmount,
                TrimmedBlock = this.TrimmedBlock
            };        

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.SystemFeeAmount = reader.ReadInt64();
            this.TrimmedBlock = reader.ReadSerializable<TrimmedBlock>();
        }

        void ICloneable<BlockState>.FromReplica(BlockState replica)
        {
            this.SystemFeeAmount = replica.SystemFeeAmount;
            this.TrimmedBlock = replica.TrimmedBlock;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.SystemFeeAmount);
            writer.Write(this.TrimmedBlock);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["sysfee_amount"] = this.SystemFeeAmount.ToString();
            json["trimmed"] = this.TrimmedBlock.ToJson();
            return json;
        }
    }
}
