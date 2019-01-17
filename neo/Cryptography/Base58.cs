using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Neo.Cryptography
{
    public static class Base58
    {
        public const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] Decode(string input)
        {
            var decoded = BigInteger.Zero;
            for (int i = input.Length - 1; i >= 0; i--)
            {
                int index = Alphabet.IndexOf(input[i]);
                if (index == -1)
                {
                    throw new FormatException("Not a valid base58 input");
                }

                decoded += index * BigInteger.Pow(58, input.Length - 1 - i);
            }

            var bytes = decoded.ToByteArray();
            Array.Reverse(bytes);

            var shouldStripSignByte = bytes.Length > 1 && bytes[0] == 0 && bytes[1] >= 0x80;
            var leadingZeros = GetLeadingZeros(input);

            var resultLength = bytes.Length - (shouldStripSignByte ? 1 : 0) + leadingZeros;
            var result = new byte[resultLength];
            Array.Copy(bytes, shouldStripSignByte ? 1 : 0, result, leadingZeros, result.Length - leadingZeros);

            return result;
        }

        public static string Encode(byte[] input)
        {
            var value = new BigInteger(new byte[1].Concat(input).Reverse().ToArray());
            var sb = new StringBuilder();
            while (value >= 58)
            {
                var mod = value % 58;
                sb.Insert(0, Base58.Alphabet[(int)mod]);
                value /= 58;
            }

            sb.Insert(0, Base58.Alphabet[(int)value]);
            foreach (var b in input)
            {
                if (b == 0)
                {
                    sb.Insert(0, Base58.Alphabet[0]);
                }
                else
                {
                    break;
                }
            }

            return sb.ToString();
        }

        private static int GetLeadingZeros(string input)
        {
            var leadingZeros = 0;
            for (int i = 0; i < input.Length && input[i] == Base58.Alphabet[0]; i++)
            {
                leadingZeros++;
            }

            return leadingZeros;
        }
    }
}
