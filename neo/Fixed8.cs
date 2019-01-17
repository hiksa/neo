using System;
using System.Globalization;
using System.IO;
using Neo.IO;

namespace Neo
{
    /// <summary>
    /// Accurate to 10^-8 64-bit fixed-point numbers minimize rounding errors.
    /// By controlling the accuracy of the multiplier, rounding errors can be completely eliminated.
    /// </summary>
    public struct Fixed8 : IComparable<Fixed8>, IEquatable<Fixed8>, IFormattable, ISerializable
    {
        public static readonly Fixed8 MaxValue = new Fixed8 { Value = long.MaxValue };

        public static readonly Fixed8 MinValue = new Fixed8 { Value = long.MinValue };

        public static readonly Fixed8 One = new Fixed8 { Value = Fixed8.Decimals };

        public static readonly Fixed8 Satoshi = new Fixed8 { Value = 1 };

        public static readonly Fixed8 Zero = default(Fixed8);

        internal long Value;

        private const long Decimals = 100_000_000;

        public Fixed8(long data)
        {
            this.Value = data;
        }

        public int Size => sizeof(long);

        public static Fixed8 FromDecimal(decimal value)
        {
            value *= Fixed8.Decimals;
            if (value < long.MinValue || value > long.MaxValue)
            {
                throw new OverflowException();
            }

            return new Fixed8 { Value = (long)value };
        }

        public static Fixed8 Max(Fixed8 first, params Fixed8[] others)
        {
            foreach (var other in others)
            {
                if (first.CompareTo(other) < 0)
                {
                    first = other;
                }
            }

            return first;
        }

        public static Fixed8 Min(Fixed8 first, params Fixed8[] others)
        {
            foreach (var other in others)
            {
                if (first.CompareTo(other) > 0)
                {
                    first = other;
                }
            }

            return first;
        }

        public static Fixed8 Parse(string s) =>
            Fixed8.FromDecimal(decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));

        public static bool TryParse(string input, out Fixed8 result)
        {
            decimal d;
            if (!decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                result = default(Fixed8);
                return false;
            }

            d *= Fixed8.Decimals;
            if (d < long.MinValue || d > long.MaxValue)
            {
                result = default(Fixed8);
                return false;
            }

            result = new Fixed8
            {
                Value = (long)d
            };

            return true;
        }

        public static explicit operator decimal(Fixed8 value) => value.Value / (decimal)Decimals;

        public static explicit operator long(Fixed8 value) => value.Value / Decimals;

        public static bool operator ==(Fixed8 x, Fixed8 y) => x.Equals(y);

        public static bool operator !=(Fixed8 x, Fixed8 y) => !x.Equals(y);

        public static bool operator >(Fixed8 x, Fixed8 y) => x.CompareTo(y) > 0;

        public static bool operator <(Fixed8 x, Fixed8 y) => x.CompareTo(y) < 0;

        public static bool operator >=(Fixed8 x, Fixed8 y) => x.CompareTo(y) >= 0;

        public static bool operator <=(Fixed8 x, Fixed8 y) => x.CompareTo(y) <= 0;

        public static Fixed8 operator *(Fixed8 x, Fixed8 y)
        {
            const ulong QUO = (1ul << 63) / (Fixed8.Decimals >> 1);
            const ulong REM = ((1ul << 63) % (Fixed8.Decimals >> 1)) << 1;

            int sign = Math.Sign(x.Value) * Math.Sign(y.Value);
            ulong ux = (ulong)Math.Abs(x.Value);
            ulong uy = (ulong)Math.Abs(y.Value);
            ulong xh = ux >> 32;
            ulong xl = ux & 0x00000000fffffffful;
            ulong yh = uy >> 32;
            ulong yl = uy & 0x00000000fffffffful;
            ulong rh = xh * yh;
            ulong rm = (xh * yl) + (xl * yh);
            ulong rl = xl * yl;
            ulong rmh = rm >> 32;
            ulong rml = rm << 32;
            rh += rmh;
            rl += rml;
            if (rl < rml)
            {
                ++rh;
            }

            if (rh >= Fixed8.Decimals)
            {
                throw new OverflowException();
            }

            ulong rd = (rh * REM) + rl;
            if (rd < rl)
            {
                ++rh;
            }

            ulong r = (rh * QUO) + (rd / Fixed8.Decimals);
            x.Value = (long)r * sign;
            return x;
        }

        public static Fixed8 operator *(Fixed8 x, long y)
        {
            x.Value = checked(x.Value * y);
            return x;
        }

        public static Fixed8 operator /(Fixed8 x, long y)
        {
            x.Value /= y;
            return x;
        }

        public static Fixed8 operator +(Fixed8 x, Fixed8 y)
        {
            x.Value = checked(x.Value + y.Value);
            return x;
        }

        public static Fixed8 operator -(Fixed8 x, Fixed8 y)
        {
            x.Value = checked(x.Value - y.Value);
            return x;
        }

        public static Fixed8 operator -(Fixed8 value)
        {
            value.Value = -value.Value;
            return value;
        }

        public long GetData() => this.Value;

        void ISerializable.Serialize(BinaryWriter writer) =>
            writer.Write(this.Value);

        public Fixed8 Abs() => this.Value >= 0
            ? this
            : new Fixed8
            {
                Value = -this.Value
            };

        public Fixed8 Ceiling()
        {
            long remainder = this.Value % Decimals;
            if (remainder == 0)
            {
                return this;
            }

            if (remainder > 0)
            {
                return new Fixed8 { Value = this.Value - remainder + Decimals };
            }
            else
            {
                return new Fixed8 { Value = this.Value - remainder };
            }
        }

        public int CompareTo(Fixed8 other) => this.Value.CompareTo(other.Value);

        void ISerializable.Deserialize(BinaryReader reader) =>
            this.Value = reader.ReadInt64();

        public bool Equals(Fixed8 other) => this.Value.Equals(other.Value);

        public string ToString(string format) => ((decimal)this).ToString(format);

        public string ToString(string format, IFormatProvider formatProvider) =>
            ((decimal)this).ToString(format, formatProvider);

        public override int GetHashCode() => this.Value.GetHashCode();

        public override string ToString() => ((decimal)this).ToString(CultureInfo.InvariantCulture);

        public override bool Equals(object obj)
        {
            if (!(obj is Fixed8))
            {
                return false;
            }

            return this.Equals((Fixed8)obj);
        }
    }
}
