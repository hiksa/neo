using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class FilterAddPayload : ISerializable
    {
        public byte[] Data { get; private set; }

        public int Size => this.Data.GetVarSize();

        void ISerializable.Deserialize(BinaryReader reader) => 
            this.Data = reader.ReadVarBytes(520);

        void ISerializable.Serialize(BinaryWriter writer) => 
            writer.WriteVarBytes(this.Data);
    }
}
