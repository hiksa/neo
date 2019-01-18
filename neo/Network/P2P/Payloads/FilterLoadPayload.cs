using System;
using System.IO;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class FilterLoadPayload : ISerializable
    {
        public byte[] Filter { get; private set; }

        public byte K { get; private set; }

        public uint Tweak { get; private set; }

        public int Size => this.Filter.GetVarSize() + sizeof(byte) + sizeof(uint);

        public static FilterLoadPayload Create(BloomFilter filter)
        {
            var buffer = new byte[filter.M / 8];
            filter.GetBits(buffer);

            return new FilterLoadPayload
            {
                Filter = buffer,
                K = (byte)filter.K,
                Tweak = filter.Tweak
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Filter = reader.ReadVarBytes(36000);
            this.K = reader.ReadByte();

            if (this.K > 50)
            {
                throw new FormatException();
            }

            this.Tweak = reader.ReadUInt32();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.WriteVarBytes(this.Filter);
            writer.Write(this.K);
            writer.Write(this.Tweak);
        }
    }
}
