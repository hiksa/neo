using System.Collections.Generic;
using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Ledger.States
{
    public class SpentCoinState : StateBase, ICloneable<SpentCoinState>
    {
        public UInt256 TransactionHash;
        public uint TransactionHeight;
        public Dictionary<ushort, uint> Items;

        public override int Size => base.Size + this.TransactionHash.Size + sizeof(uint)
            + IO.Helper.GetVarSize(this.Items.Count) + (this.Items.Count * (sizeof(ushort) + sizeof(uint)));

        SpentCoinState ICloneable<SpentCoinState>.Clone() => 
            new SpentCoinState
            {
                TransactionHash = this.TransactionHash,
                TransactionHeight = this.TransactionHeight,
                Items = new Dictionary<ushort, uint>(this.Items)
            };

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.TransactionHash = reader.ReadSerializable<UInt256>();
            this.TransactionHeight = reader.ReadUInt32();

            var count = (int)reader.ReadVarInt();
            this.Items = new Dictionary<ushort, uint>(count);

            for (int i = 0; i < count; i++)
            {
                ushort index = reader.ReadUInt16();
                uint height = reader.ReadUInt32();
                this.Items.Add(index, height);
            }
        }

        void ICloneable<SpentCoinState>.FromReplica(SpentCoinState replica)
        {
            this.TransactionHash = replica.TransactionHash;
            this.TransactionHeight = replica.TransactionHeight;
            this.Items = replica.Items;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(this.TransactionHash);
            writer.Write(this.TransactionHeight);
            writer.WriteVarInt(this.Items.Count);

            foreach (var pair in this.Items)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
        }
    }
}
