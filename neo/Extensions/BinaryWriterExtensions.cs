using System;
using System.IO;
using System.Text;
using Neo.IO;

namespace Neo.Extensions
{
    public static class BinaryWriterExtensions
    {
        public static void Write(this BinaryWriter writer, ISerializable value) =>
            value.Serialize(writer);        

        public static void Write<T>(this BinaryWriter writer, T[] value) where T : ISerializable
        {
            writer.WriteVarInt(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                value[i].Serialize(writer);
            }
        }

        public static void WriteBytesWithGrouping(this BinaryWriter writer, byte[] value)
        {
            const int GroupSize = 16;

            var index = 0;
            var remaining = value.Length;
            while (remaining >= GroupSize)
            {
                writer.Write(value, index, GroupSize);
                writer.Write((byte)0);
                index += GroupSize;
                remaining -= GroupSize;
            }

            if (remaining > 0)
            {
                writer.Write(value, index, remaining);
            }

            var padding = GroupSize - remaining;
            for (int i = 0; i < padding; i++)
            {
                writer.Write((byte)0);
            }

            writer.Write((byte)padding);
        }

        public static void WriteFixedString(this BinaryWriter writer, string value, int length)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > length)
            {
                throw new ArgumentException();
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > length)
            {
                throw new ArgumentException();
            }

            writer.Write(bytes);
            if (bytes.Length < length)
            {
                writer.Write(new byte[length - bytes.Length]);
            }
        }

        public static void WriteVarBytes(this BinaryWriter writer, byte[] value)
        {
            writer.WriteVarInt(value.Length);
            writer.Write(value);
        }

        public static void WriteVarInt(this BinaryWriter writer, long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (value < 0xFD)
            {
                writer.Write((byte)value);
            }
            else if (value <= 0xFFFF)
            {
                writer.Write((byte)0xFD);
                writer.Write((ushort)value);
            }
            else if (value <= 0xFFFFFFFF)
            {
                writer.Write((byte)0xFE);
                writer.Write((uint)value);
            }
            else
            {
                writer.Write((byte)0xFF);
                writer.Write(value);
            }
        }

        public static void WriteVarString(this BinaryWriter writer, string value) =>
            writer.WriteVarBytes(Encoding.UTF8.GetBytes(value));
    }
}
