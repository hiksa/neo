using System;
using System.IO;
using Neo.IO;
using Neo.IO.Caching;

namespace Neo.Consensus
{
    internal abstract class ConsensusMessage : ISerializable
    {
        /// <summary>
        /// Reflection cache for ConsensusMessageType
        /// </summary>
        private static ReflectionCache<byte> reflectionCache = ReflectionCache<byte>.CreateFromEnum<ConsensusMessageType>();
        
        protected ConsensusMessage(ConsensusMessageType type)
        {
            this.Type = type;
        }

        public virtual int Size => sizeof(ConsensusMessageType) + sizeof(byte);

        public byte ViewNumber { get; set; }

        public ConsensusMessageType Type { get; private set; }

        public static ConsensusMessage DeserializeFrom(byte[] data)
        {
            var message = reflectionCache.CreateInstance<ConsensusMessage>(data[0]);
            if (message == null)
            {
                throw new FormatException();
            }

            using (MemoryStream ms = new MemoryStream(data, false))
            using (BinaryReader r = new BinaryReader(ms))
            {
                message.Deserialize(r);
            }

            return message;
        }

        public virtual void Deserialize(BinaryReader reader)
        {
            if (this.Type != (ConsensusMessageType)reader.ReadByte())
            {
                throw new FormatException();
            }

            this.ViewNumber = reader.ReadByte();
        }

        public virtual void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)this.Type);
            writer.Write(this.ViewNumber);
        }
    }
}
