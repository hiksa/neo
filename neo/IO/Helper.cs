using System.IO;
using System.Text;

namespace Neo.IO
{
    public static class Helper
    {
        internal static int GetVarSize(int value)
        {
            if (value < 0xFD)
                return sizeof(byte);
            else if (value <= 0xFFFF)
                return sizeof(byte) + sizeof(ushort);
            else
                return sizeof(byte) + sizeof(uint);
        }

        internal static int GetVarSize(this string value)
        {
            var size = Encoding.UTF8.GetByteCount(value);
            return GetVarSize(size) + size;
        }

        public static byte[] ToArray(this ISerializable value)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                value.Serialize(writer);
                writer.Flush();

                return ms.ToArray();
            }
        }
    }
}
