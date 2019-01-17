using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger.States;

namespace Neo.Ledger
{
    public class StorageItem : StateBase, ICloneable<StorageItem>
    {
        public byte[] Value;
        public bool IsConstant;

        public override int Size => base.Size + this.Value.GetVarSize() + sizeof(bool);

        StorageItem ICloneable<StorageItem>.Clone() =>
            new StorageItem
            {
                Value = this.Value,
                IsConstant = this.IsConstant
            };        

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.Value = reader.ReadVarBytes();
            this.IsConstant = reader.ReadBoolean();
        }

        void ICloneable<StorageItem>.FromReplica(StorageItem replica)
        {
            this.Value = replica.Value;
            this.IsConstant = replica.IsConstant;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.WriteVarBytes(this.Value);
            writer.Write(this.IsConstant);
        }
    }
}
