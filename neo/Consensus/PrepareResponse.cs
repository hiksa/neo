using System.IO;

namespace Neo.Consensus
{
    internal class PrepareResponse : ConsensusMessage
    {
        public PrepareResponse()
            : base(ConsensusMessageType.PrepareResponse)
        {
        }

        public override int Size => base.Size + this.Signature.Length;

        public byte[] Signature { get; set; }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.Signature = reader.ReadBytes(64);
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.Signature);
        }
    }
}
