using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Neo.Extensions;

namespace Neo.Cryptography.ECC
{
    public class ECDsa
    {
        private readonly byte[] privateKey;
        private readonly ECPoint publicKey;
        private readonly ECCurve curve;

        public ECDsa(byte[] privateKey, ECCurve curve)
            : this(curve.G * privateKey)
        {
            this.privateKey = privateKey;
        }

        public ECDsa(ECPoint publicKey)
        {
            this.publicKey = publicKey;
            this.curve = publicKey.curve;
        }

        public BigInteger[] GenerateSignature(byte[] message)
        {
            if (this.privateKey == null)
            {
                throw new InvalidOperationException("Private key is required in order to generate a signature.");
            }

            BigInteger e = this.CalculateE(this.curve.N, message);
            BigInteger d = new BigInteger(this.privateKey.Reverse().Concat(new byte[1]).ToArray());
            BigInteger r;
            BigInteger s;

            using (var rng = RandomNumberGenerator.Create())
            {
                do
                {
                    BigInteger k;
                    do
                    {
                        do
                        {
                            k = rng.NextBigInteger(this.curve.N.GetBitLength());
                        }
                        while (k.Sign == 0 || k.CompareTo(this.curve.N) >= 0);

                        var point = ECPoint.Multiply(this.curve.G, k);
                        var x = point.x.Value;
                        r = x.Mod(this.curve.N);
                    }
                    while (r.Sign == 0);

                    s = (k.ModInverse(this.curve.N) * (e + (d * r))).Mod(this.curve.N);
                    if (s > this.curve.N / 2)
                    {
                        s = this.curve.N - s;
                    }
                }
                while (s.Sign == 0);
            }

            return new BigInteger[] { r, s };
        }

        public bool VerifySignature(byte[] message, BigInteger r, BigInteger s)
        {
            if (r.Sign < 1 || s.Sign < 1 || r.CompareTo(this.curve.N) >= 0 || s.CompareTo(this.curve.N) >= 0)
            {
                return false;
            }

            var e = this.CalculateE(this.curve.N, message);
            var c = s.ModInverse(this.curve.N);
            var u1 = (e * c).Mod(this.curve.N);
            var u2 = (r * c).Mod(this.curve.N);
            var point = ECDsa.SumOfTwoMultiplies(this.curve.G, u1, this.publicKey, u2);
            var v = point.x.Value.Mod(this.curve.N);
            return v.Equals(r);
        }

        private static ECPoint SumOfTwoMultiplies(ECPoint p, BigInteger k, ECPoint q, BigInteger l)
        {
            var longerLength = Math.Max(k.GetBitLength(), l.GetBitLength());
            var pointZ = p + q;
            var pointR = p.curve.Infinity;
            for (int i = longerLength - 1; i >= 0; --i)
            {
                pointR = pointR.Twice();
                if (k.BitAtIndexIsOne(i))
                {
                    if (l.BitAtIndexIsOne(i))
                    {
                        pointR = pointR + pointZ;
                    }
                    else
                    {
                        pointR = pointR + p;
                    }
                }
                else if(l.BitAtIndexIsOne(i))
                {
                    pointR = pointR + q;
                }
            }

            return pointR;
        }

        private BigInteger CalculateE(BigInteger n, byte[] message)
        {
            var messageBitLength = message.Length * 8;
            var trunc = new BigInteger(message.Reverse().Concat(new byte[1]).ToArray());
            if (n.GetBitLength() < messageBitLength)
            {
                trunc >>= messageBitLength - n.GetBitLength();
            }

            return trunc;
        }
    }
}
