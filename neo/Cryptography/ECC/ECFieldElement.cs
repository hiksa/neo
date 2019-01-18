using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Neo.Extensions;

namespace Neo.Cryptography.ECC
{
    internal class ECFieldElement : IComparable<ECFieldElement>, IEquatable<ECFieldElement>
    {
        internal readonly BigInteger Value;
        private readonly ECCurve curve;

        public ECFieldElement(BigInteger value, ECCurve curve)
        {
            if (value >= curve.Q)
            {
                throw new ArgumentException("x value too large in field element");
            }

            this.Value = value;
            this.curve = curve;
        }

        public int CompareTo(ECFieldElement other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return 0;
            }

            return this.Value.CompareTo(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj == this)
            {
                return true;
            }

            var other = obj as ECFieldElement;
            if (other == null)
            {
                return false;
            }

            return this.Equals(other);
        }

        public bool Equals(ECFieldElement other) => this.Value.Equals(other.Value);

        private static BigInteger[] FastLucasSequence(
            BigInteger p, 
            BigInteger P, 
            BigInteger Q, 
            BigInteger k)
        {
            int n = k.GetBitLength();
            int s = k.GetLowestSetBit();

            Debug.Assert(k.BitAtIndexIsOne(s));

            BigInteger Uh = 1;
            BigInteger Vl = 2;
            BigInteger Vh = P;
            BigInteger Ql = 1;
            BigInteger Qh = 1;

            for (int j = n - 1; j >= s + 1; --j)
            {
                Ql = (Ql * Qh).Mod(p);

                if (k.BitAtIndexIsOne(j))
                {
                    Qh = (Ql * Q).Mod(p);
                    Uh = (Uh * Vh).Mod(p);
                    Vl = ((Vh * Vl) - (P * Ql)).Mod(p);
                    Vh = ((Vh * Vh) - (Qh << 1)).Mod(p);
                }
                else
                {
                    Qh = Ql;
                    Uh = ((Uh * Vl) - Ql).Mod(p);
                    Vh = ((Vh * Vl) - (P * Ql)).Mod(p);
                    Vl = ((Vl * Vl) - (Ql << 1)).Mod(p);
                }
            }

            Ql = (Ql * Qh).Mod(p);
            Qh = (Ql * Q).Mod(p);
            Uh = ((Uh * Vl) - Ql).Mod(p);
            Vl = ((Vh * Vl) - (P * Ql)).Mod(p);
            Ql = (Ql * Qh).Mod(p);

            for (int j = 1; j <= s; ++j)
            {
                Uh = Uh * Vl * p;
                Vl = ((Vl * Vl) - (Ql << 1)).Mod(p);
                Ql = (Ql * Ql).Mod(p);
            }

            return new BigInteger[] { Uh, Vl };
        }

        public override int GetHashCode() => this.Value.GetHashCode();

        public ECFieldElement Sqrt()
        {
            if (this.curve.Q.BitAtIndexIsOne(1))
            {
                var value = BigInteger.ModPow(this.Value, (this.curve.Q >> 2) + 1, this.curve.Q);
                var z = new ECFieldElement(value, this.curve);

                return z.Square().Equals(this) ? z : null;
            }

            var qMinusOne = this.curve.Q - 1;
            var legendreExponent = qMinusOne >> 1;
            if (BigInteger.ModPow(this.Value, legendreExponent, this.curve.Q) != 1)
            {
                return null;
            }

            var u = qMinusOne >> 2;
            var k = (u << 1) + 1;
            var Q = this.Value;
            var fourQ = (Q << 2).Mod(this.curve.Q);
            BigInteger U;
            BigInteger V;

            do
            {
                var random = new Random();
                BigInteger P;

                do
                {
                    P = random.NextBigInteger(this.curve.Q.GetBitLength());
                }
                while (P >= this.curve.Q || BigInteger.ModPow((P * P) - fourQ, legendreExponent, this.curve.Q) != qMinusOne);

                var lucasSequence = ECFieldElement.FastLucasSequence(this.curve.Q, P, Q, k);
                U = lucasSequence[0];
                V = lucasSequence[1];

                if ((V * V).Mod(this.curve.Q) == fourQ)
                {
                    if (V.BitAtIndexIsOne(0))
                    {
                        V += this.curve.Q;
                    }

                    V >>= 1;
                    Debug.Assert((V * V).Mod(this.curve.Q) == this.Value);
                    return new ECFieldElement(V, this.curve);
                }
            }
            while (U.Equals(BigInteger.One) || U.Equals(qMinusOne));

            return null;
        }

        public ECFieldElement Square() => new ECFieldElement((this.Value * this.Value).Mod(this.curve.Q), this.curve);

        public byte[] ToByteArray()
        {
            var data = this.Value.ToByteArray();
            if (data.Length == 32)
            {
                return data.Reverse().ToArray();
            }

            if (data.Length > 32)
            {
                return data.Take(32).Reverse().ToArray();
            }

            return Enumerable
                .Repeat<byte>(0, 32 - data.Length)
                .Concat(data.Reverse())
                .ToArray();
        }

        public static ECFieldElement operator -(ECFieldElement x) =>
            new ECFieldElement((-x.Value).Mod(x.curve.Q), x.curve);

        public static ECFieldElement operator *(ECFieldElement x, ECFieldElement y) =>
            new ECFieldElement((x.Value * y.Value).Mod(x.curve.Q), x.curve);

        public static ECFieldElement operator /(ECFieldElement x, ECFieldElement y) =>
            new ECFieldElement((x.Value * y.Value.ModInverse(x.curve.Q)).Mod(x.curve.Q), x.curve);

        public static ECFieldElement operator +(ECFieldElement x, ECFieldElement y) =>
            new ECFieldElement((x.Value + y.Value).Mod(x.curve.Q), x.curve);
        
        public static ECFieldElement operator -(ECFieldElement x, ECFieldElement y) =>
            new ECFieldElement((x.Value - y.Value).Mod(x.curve.Q), x.curve);
    }
}
