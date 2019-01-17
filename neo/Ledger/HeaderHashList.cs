using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger.States;

namespace Neo.Ledger
{
    public class HeaderHashList : StateBase, ICloneable<HeaderHashList>
    {
        public UInt256[] Hashes;

        public override int Size => base.Size + this.Hashes.GetVarSize();

        HeaderHashList ICloneable<HeaderHashList>.Clone() => new HeaderHashList { Hashes = this.Hashes };

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.Hashes = reader.ReadSerializableArray<UInt256>();
        }

        void ICloneable<HeaderHashList>.FromReplica(HeaderHashList replica)
        {
            this.Hashes = replica.Hashes;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.Hashes);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["hashes"] = this.Hashes.Select(p => (JObject)p.ToString()).ToArray();
            return json;
        }
    }
}
