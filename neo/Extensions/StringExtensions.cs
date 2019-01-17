using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using Neo.Cryptography;

namespace Neo.Extensions
{
    public static class StringExtensions
    {
        private static readonly ConcurrentDictionary<string, uint> MethodHashes = new ConcurrentDictionary<string, uint>();

        public static UInt160 ToScriptHash(this string address)
        {
            var data = address.Base58CheckDecode();
            if (data.Length != 21)
            {
                throw new FormatException("The address should be a string with 21 characters length.");
            }

            if (data[0] != ProtocolSettings.Default.AddressVersion)
            {
                throw new FormatException();
            }

            return new UInt160(data.Skip(1).ToArray());
        }

        public static uint ToInteropMethodHash(this string method) =>
            MethodHashes.GetOrAdd(method, p => BitConverter.ToUInt32(Encoding.ASCII.GetBytes(p).Sha256(), 0));

        public static byte[] HexToBytes(this string value)
        {
            if (value == null || value.Length == 0)
            {
                return new byte[0];
            }

            if (value.Length % 2 == 1)
            {
                throw new FormatException("Value should have even length.");
            }

            var result = new byte[value.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier);
            }

            return result;
        }
    }
}
