using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Cryptography.ECC
{
    public class ECPoint : IComparable<ECPoint>, IEquatable<ECPoint>, ISerializable
    {
        internal readonly ECCurve curve;

        internal ECFieldElement x;
        internal ECFieldElement y;

        public ECPoint()
            : this(null, null, ECCurve.Secp256r1)
        {
        }

        internal ECPoint(ECFieldElement x, ECFieldElement y, ECCurve curve)
        {
            if ((x != null && y == null) || (x == null && y != null))
            {
                throw new ArgumentException("Exactly one of the field elements is null");
            }

            this.x = x;
            this.y = y;
            this.curve = curve;
        }

        public bool IsInfinity => this.x == null && this.y == null;

        public int Size => this.IsInfinity ? 1 : 33;

        public static ECPoint operator -(ECPoint x) =>
            new ECPoint(x.x, -x.y, x.curve);

        public static ECPoint operator *(ECPoint p, byte[] n)
        {
            if (p == null || n == null)
            {
                throw new ArgumentNullException();
            }

            if (n.Length != 32)
            {
                throw new ArgumentException();
            }

            if (p.IsInfinity)
            {
                return p;
            }

            var k = new BigInteger(n.Reverse().Concat(new byte[1]).ToArray());
            if (k.Sign == 0)
            {
                return p.curve.Infinity;
            }

            return ECPoint.Multiply(p, k);
        }

        public static ECPoint operator +(ECPoint x, ECPoint y)
        {
            if (x.IsInfinity)
            {
                return y;
            }

            if (y.IsInfinity)
            {
                return x;
            }

            if (x.x.Equals(y.x))
            {
                if (x.y.Equals(y.y))
                {
                    return x.Twice();
                }

                Debug.Assert(x.y.Equals(-y.y));
                return x.curve.Infinity;
            }

            var gamma = (y.y - x.y) / (y.x - x.x);
            var x3 = gamma.Square() - x.x - y.x;
            var y3 = (gamma * (x.x - x3)) - x.y;

            var result = new ECPoint(x3, y3, x.curve);
            return result;
        }

        public static ECPoint operator -(ECPoint x, ECPoint y)
        {
            if (y.IsInfinity)
            {
                return x;
            }

            return x + (-y);
        }

        public static ECPoint DecodePoint(byte[] encoded, ECCurve curve)
        {
            ECPoint p = null;
            var expectedLength = (curve.Q.GetBitLength() + 7) / 8;
            switch (encoded[0])
            {
                case 0x00: // infinity
                    {
                        if (encoded.Length != 1)
                        {
                            throw new FormatException("Incorrect length for infinity encoding");
                        }

                        p = curve.Infinity;
                        break;
                    }

                case 0x02: // compressed
                case 0x03: // compressed
                    {
                        if (encoded.Length != (expectedLength + 1))
                        {
                            throw new FormatException("Incorrect length for compressed encoding");
                        }

                        int yTilde = encoded[0] & 1;
                        var x1 = new BigInteger(encoded.Skip(1).Reverse().Concat(new byte[1]).ToArray());
                        p = DecompressPoint(yTilde, x1, curve);
                        break;
                    }

                case 0x04: // uncompressed
                case 0x06: // hybrid
                case 0x07: // hybrid
                    {
                        if (encoded.Length != (2 * expectedLength) + 1)
                        {
                            throw new FormatException("Incorrect length for uncompressed/hybrid encoding");
                        }

                        var x1 = new BigInteger(encoded.Skip(1).Take(expectedLength).Reverse().Concat(new byte[1]).ToArray());
                        var y1 = new BigInteger(encoded.Skip(1 + expectedLength).Reverse().Concat(new byte[1]).ToArray());
                        p = new ECPoint(new ECFieldElement(x1, curve), new ECFieldElement(y1, curve), curve);
                        break;
                    }

                default:
                    throw new FormatException("Invalid point encoding " + encoded[0]);
            }

            return p;
        }

        public static ECPoint DeserializeFrom(BinaryReader reader, ECCurve curve)
        {
            var expectedLength = (curve.Q.GetBitLength() + 7) / 8;
            var buffer = new byte[1 + (expectedLength * 2)];
            buffer[0] = reader.ReadByte();

            switch (buffer[0])
            {
                case 0x00:
                    return curve.Infinity;
                case 0x02:
                case 0x03:
                    reader.Read(buffer, 1, expectedLength);
                    return ECPoint.DecodePoint(buffer.Take(1 + expectedLength).ToArray(), curve);
                case 0x04:
                case 0x06:
                case 0x07:
                    reader.Read(buffer, 1, expectedLength * 2);
                    return ECPoint.DecodePoint(buffer, curve);
                default:
                    throw new FormatException("Invalid point encoding " + buffer[0]);
            }
        }

        public static ECPoint FromBytes(byte[] pubkey, ECCurve curve)
        {
            switch (pubkey.Length)
            {
                case 33:
                case 65:
                    return ECPoint.DecodePoint(pubkey, curve);
                case 64:
                case 72:
                    {
                        var encoded = new byte[] { 0x04 }
                            .Concat(pubkey.Skip(pubkey.Length - 64))
                            .ToArray();

                        return ECPoint.DecodePoint(encoded, curve);
                    }
                case 96:
                case 104:
                    {
                        var encoded = new byte[] { 0x04 }
                            .Concat(pubkey.Skip(pubkey.Length - 96).Take(64))
                            .ToArray();

                        return ECPoint.DecodePoint(encoded, curve);
                    }
                default:
                    throw new FormatException("Invalid public key.");
            }
        }

        public static bool TryParse(string value, ECCurve curve, out ECPoint point)
        {
            try
            {
                point = ECPoint.Parse(value, curve);
                return true;
            }
            catch (FormatException)
            {
                point = null;
                return false;
            }
        }

        public static ECPoint Parse(string value, ECCurve curve) => 
            ECPoint.DecodePoint(value.HexToBytes(), curve);

        void ISerializable.Deserialize(BinaryReader reader)
        {
            var point = ECPoint.DeserializeFrom(reader, this.curve);
            this.x = point.x;
            this.y = point.y;
        }

        public int CompareTo(ECPoint other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return 0;
            }

            var result = this.x.CompareTo(other.x);
            if (result != 0)
            {
                return result;
            }

            return this.y.CompareTo(other.y);
        }

        public byte[] EncodePoint(bool commpressed)
        {
            if (this.IsInfinity)
            {
                return new byte[1];
            }

            byte[] data;
            if (commpressed)
            {
                data = new byte[33];
            }
            else
            {
                data = new byte[65];

                var yBytes = this.y.Value.ToByteArray().Reverse().ToArray();

                Buffer.BlockCopy(yBytes, 0, data, 65 - yBytes.Length, yBytes.Length);
            }

            var xBytes = this.x.Value.ToByteArray().Reverse().ToArray();

            Buffer.BlockCopy(xBytes, 0, data, 33 - xBytes.Length, xBytes.Length);

            data[0] = commpressed ? this.y.Value.IsEven ? (byte)0x02 : (byte)0x03 : (byte)0x04;
            return data;
        }

        public bool Equals(ECPoint other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (object.ReferenceEquals(null, other))
            {
                return false;
            }

            if (this.IsInfinity && other.IsInfinity)
            {
                return true;
            }

            if (this.IsInfinity || other.IsInfinity)
            {
                return false;
            }

            return this.x.Equals(other.x) && this.y.Equals(other.y);
        }

        public override bool Equals(object obj) => this.Equals(obj as ECPoint);

        public override int GetHashCode() => this.x.GetHashCode() + this.y.GetHashCode();

        void ISerializable.Serialize(BinaryWriter writer) => writer.Write(this.EncodePoint(true));
        
        public override string ToString() => this.EncodePoint(true).ToHexString();

        internal static ECPoint Multiply(ECPoint p, BigInteger k)
        {
            // floor(log2(k))
            int m = k.GetBitLength();

            // width of the Window NAF
            sbyte width;

            // Required length of precomputation array
            int reqPreCompLen;

            // Determine optimal width and corresponding length of precomputation
            // array based on literature values
            if (m < 13)
            {
                width = 2;
                reqPreCompLen = 1;
            }
            else if (m < 41)
            {
                width = 3;
                reqPreCompLen = 2;
            }
            else if (m < 121)
            {
                width = 4;
                reqPreCompLen = 4;
            }
            else if (m < 337)
            {
                width = 5;
                reqPreCompLen = 8;
            }
            else if (m < 897)
            {
                width = 6;
                reqPreCompLen = 16;
            }
            else if (m < 2305)
            {
                width = 7;
                reqPreCompLen = 32;
            }
            else
            {
                width = 8;
                reqPreCompLen = 127;
            }

            // The length of the precomputation array
            int preCompLen = 1;

            var preComp = new ECPoint[] { p };
            var twiceP = p.Twice();

            if (preCompLen < reqPreCompLen)
            {
                // Precomputation array must be made bigger, copy existing preComp
                // array into the larger new preComp array
                var oldPreComp = preComp;
                preComp = new ECPoint[reqPreCompLen];

                Array.Copy(oldPreComp, 0, preComp, 0, preCompLen);

                for (int i = preCompLen; i < reqPreCompLen; i++)
                {
                    // Compute the new ECPoints for the precomputation array.
                    // The values 1, 3, 5, ..., 2^(width-1)-1 times p are
                    // computed
                    preComp[i] = twiceP + preComp[i - 1];
                }
            }

            // Compute the Window NAF of the desired width
            var wnaf = ECPoint.WindowNaf(width, k);
            var l = wnaf.Length;

            // Apply the Window NAF to p using the precomputed ECPoint values.
            ECPoint q = p.curve.Infinity;
            for (int i = l - 1; i >= 0; i--)
            {
                q = q.Twice();

                if (wnaf[i] != 0)
                {
                    if (wnaf[i] > 0)
                    {
                        q += preComp[(wnaf[i] - 1) / 2];
                    }
                    else
                    {
                        // wnaf[i] < 0
                        q -= preComp[(-wnaf[i] - 1) / 2];
                    }
                }
            }

            return q;
        }

        internal ECPoint Twice()
        {
            if (this.IsInfinity)
            {
                return this;
            }

            if (this.y.Value.Sign == 0)
            {
                return this.curve.Infinity;
            }

            var two = new ECFieldElement(2, this.curve);
            var three = new ECFieldElement(3, this.curve);

            var gamma = ((this.x.Square() * three) + this.curve.A) / (this.y * two);
            var x3 = gamma.Square() - (this.x * two);
            var y3 = (gamma * (this.x - x3)) - this.y;

            return new ECPoint(x3, y3, this.curve);
        }

        private static sbyte[] WindowNaf(sbyte width, BigInteger k)
        {
            var wnaf = new sbyte[k.GetBitLength() + 1];
            var pow2wB = (short)(1 << width);
            var i = 0;
            var length = 0;
            while (k.Sign > 0)
            {
                if (!k.IsEven)
                {
                    var remainder = k % pow2wB;
                    if (remainder.BitAtIndexIsOne(width - 1))
                    {
                        wnaf[i] = (sbyte)(remainder - pow2wB);
                    }
                    else
                    {
                        wnaf[i] = (sbyte)remainder;
                    }

                    k -= wnaf[i];
                    length = i;
                }
                else
                {
                    wnaf[i] = 0;
                }

                k >>= 1;
                i++;
            }

            length++;
            sbyte[] wnafShort = new sbyte[length];
            Array.Copy(wnaf, 0, wnafShort, 0, length);
            return wnafShort;
        }

        private static ECPoint DecompressPoint(int yTilde, BigInteger x1, ECCurve curve)
        {
            ECFieldElement x = new ECFieldElement(x1, curve);
            ECFieldElement alpha = (x * (x.Square() + curve.A)) + curve.B;
            ECFieldElement beta = alpha.Sqrt();

            // If we can't find a sqrt we haven't got a point on the curve
            if (beta == null)
            {
                throw new ArithmeticException("Invalid point compression");
            }

            var betaValue = beta.Value;
            var bit0 = betaValue.IsEven ? 0 : 1;

            if (bit0 != yTilde)
            {
                // Use the other root
                beta = new ECFieldElement(curve.Q - betaValue, curve);
            }

            return new ECPoint(x, beta, curve);
        }
    }
}
