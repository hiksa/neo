using System;
using System.Numerics;

namespace Neo
{
    public struct BigDecimal
    {
        private readonly BigInteger value;
        private readonly byte decimals;

        public BigDecimal(BigInteger value, byte decimals)
        {
            this.value = value;
            this.decimals = decimals;
        }

        public BigInteger Value => this.value;

        public byte Decimals => this.decimals;

        public int Sign => this.value.Sign;

        public static BigDecimal Parse(string s, byte decimals)
        {
            if (!BigDecimal.TryParse(s, decimals, out BigDecimal result))
            {
                throw new FormatException();
            }

            return result;
        }

        public static bool TryParse(string s, byte decimals, out BigDecimal result)
        {
            var e = 0;
            var index = s.IndexOfAny(new[] { 'e', 'E' });
            if (index >= 0)
            {
                if (!sbyte.TryParse(s.Substring(index + 1), out sbyte e_temp))
                {
                    result = default(BigDecimal);
                    return false;
                }

                e = e_temp;
                s = s.Substring(0, index);
            }

            index = s.IndexOf('.');
            if (index >= 0)
            {
                s = s.TrimEnd('0');
                e -= s.Length - index - 1;
                s = s.Remove(index, 1);
            }

            var ds = e + decimals;
            if (ds < 0)
            {
                result = default(BigDecimal);
                return false;
            }

            if (ds > 0)
            {
                s += new string('0', ds);
            }

            if (!BigInteger.TryParse(s, out BigInteger value))
            {
                result = default(BigDecimal);
                return false;
            }

            result = new BigDecimal(value, decimals);
            return true;
        }

        public BigDecimal ChangeDecimals(byte decimals)
        {
            if (this.decimals == decimals)
            {
                return this;
            }

            BigInteger value;
            if (this.decimals < decimals)
            {
                value = this.value * BigInteger.Pow(10, decimals - this.decimals);
            }
            else
            {
                var divisor = BigInteger.Pow(10, this.decimals - decimals);
                value = BigInteger.DivRem(this.value, divisor, out BigInteger remainder);
                if (remainder > BigInteger.Zero)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }

            return new BigDecimal(value, decimals);
        }

        public Fixed8 ToFixed8()
        {
            try
            {
                return new Fixed8((long)this.ChangeDecimals(8).value);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException(ex.Message, ex);
            }
        }

        public override string ToString()
        {
            var divisor = BigInteger.Pow(10, this.decimals);
            var result = BigInteger.DivRem(this.value, divisor, out BigInteger remainder);
            if (remainder == 0)
            {
                return result.ToString();
            }

            return $"{result}.{remainder.ToString("d" + decimals)}".TrimEnd('0');
        }
    }
}
