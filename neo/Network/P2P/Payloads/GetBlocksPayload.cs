using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class GetBlocksPayload : ISerializable
    {
        public UInt256[] HashStart { get; private set; }

        public UInt256 HashStop { get; private set; }

        public int Size => this.HashStart.GetVarSize() + this.HashStop.Size;

        public static GetBlocksPayload Create(UInt256 hashStart, UInt256 hashStop = null)
        {
            return new GetBlocksPayload
            {
                HashStart = new[] { hashStart },
                HashStop = hashStop ?? UInt256.Zero
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.HashStart = reader.ReadSerializableArray<UInt256>(16);
            this.HashStop = reader.ReadSerializable<UInt256>();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.HashStart);
            writer.Write(this.HashStop);
        }
    }
}
