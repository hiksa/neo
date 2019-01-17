using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Neo.Extensions
{
    public static class BigIntegerExtensions
    {
        public static int GetBitLength(this BigInteger i)
        {
            var b = i.ToByteArray();
            var w = i.Sign > 0
                ? b[b.Length - 1]
                : 255 - b[b.Length - 1];

            return ((b.Length - 1) * 8) + BigIntegerExtensions.BitLength(w);
        }

        public static int GetLowestSetBit(this BigInteger integer)
        {
            if (integer.Sign == 0)
            {
                return -1;
            }

            var b = integer.ToByteArray();
            var w = 0;
            while (b[w] == 0)
            {
                w++;
            }

            for (int i = 0; i < 8; i++)
            {
                if ((b[w] & (1 << i)) > 0)
                {
                    return i + (w * 8);
                }
            }

            throw new Exception();
        }

        public static BigInteger Mod(this BigInteger x, BigInteger y)
        {
            x %= y;
            if (x.Sign < 0)
            {
                x += y;
            }

            return x;
        }

        public static BigInteger ModInverse(this BigInteger a, BigInteger n)
        {
            BigInteger i = n;
            BigInteger v = 0;
            BigInteger d = 1;

            while (a > 0)
            {
                BigInteger t = i / a;
                BigInteger x = a;

                a = i % x;
                i = x;
                x = d;
                d = v - (t * x);
                v = x;
            }

            v %= n;
            if (v < 0)
            {
                v = (v + n) % n;
            }

            return v;
        }

        public static bool BitAtIndexIsOne(this BigInteger i, int index) =>
            (i & (BigInteger.One << index)) > BigInteger.Zero;

        private static int BitLength(int w)
        {
            return w < 1 << 15 ? (w < 1 << 7
                ? (w < 1 << 3 ? (w < 1 << 1
                ? (w < 1 << 0 ? (w < 0 ? 32 : 0) : 1)
                : (w < 1 << 2 ? 2 : 3)) : (w < 1 << 5
                ? (w < 1 << 4 ? 4 : 5)
                : (w < 1 << 6 ? 6 : 7)))
                : (w < 1 << 11
                ? (w < 1 << 9 ? (w < 1 << 8 ? 8 : 9) : (w < 1 << 10 ? 10 : 11))
                : (w < 1 << 13 ? (w < 1 << 12 ? 12 : 13) : (w < 1 << 14 ? 14 : 15)))) : (w < 1 << 23 ? (w < 1 << 19
                ? (w < 1 << 17 ? (w < 1 << 16 ? 16 : 17) : (w < 1 << 18 ? 18 : 19))
                : (w < 1 << 21 ? (w < 1 << 20 ? 20 : 21) : (w < 1 << 22 ? 22 : 23))) : (w < 1 << 27
                ? (w < 1 << 25 ? (w < 1 << 24 ? 24 : 25) : (w < 1 << 26 ? 26 : 27))
                : (w < 1 << 29 ? (w < 1 << 28 ? 28 : 29) : (w < 1 << 30 ? 30 : 31))));
        }
    }
}
