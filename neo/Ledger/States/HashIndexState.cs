using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;

namespace Neo.Ledger.States
{
    public class HashIndexState : StateBase, ICloneable<HashIndexState>
    {
        public UInt256 Hash = UInt256.Zero;
        public uint Index = uint.MaxValue;

        public override int Size => base.Size + Hash.Size + sizeof(uint);

        HashIndexState ICloneable<HashIndexState>.Clone()
        {
            return new HashIndexState
            {
                Hash = Hash,
                Index = Index
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.Hash = reader.ReadSerializable<UInt256>();
            this.Index = reader.ReadUInt32();
        }

        void ICloneable<HashIndexState>.FromReplica(HashIndexState replica)
        {
            this.Hash = replica.Hash;
            this.Index = replica.Index;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(this.Hash);
            writer.Write(this.Index);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["hash"] = this.Hash.ToString();
            json["index"] = this.Index;
            return json;
        }
    }
}
