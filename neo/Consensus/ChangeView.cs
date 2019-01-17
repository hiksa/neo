using System;
using System.IO;

namespace Neo.Consensus
{
    internal class ChangeView : ConsensusMessage
    {
        public ChangeView()
            : base(ConsensusMessageType.ChangeView)
        {
        }

        public byte NewViewNumber { get; set; }

        public override int Size => base.Size + sizeof(byte);

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.NewViewNumber = reader.ReadByte();
            if (this.NewViewNumber == 0)
            {
                throw new FormatException();
            }
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.NewViewNumber);
        }
    }
}
