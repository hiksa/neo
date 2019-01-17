using System;
using System.Globalization;
using System.Linq;
using Neo.Extensions;

namespace Neo
{
    /// <summary>
    /// This class stores a 256 bit unsigned int, represented as a 32-byte little-endian byte array
    /// </summary>
    public class UInt256 : UIntBase, IComparable<UInt256>, IEquatable<UInt256>
    {
        public static readonly UInt256 Zero = new UInt256();
        
        /// <summary>
        /// The empty constructor stores a null byte array
        /// </summary>
        public UInt256()
            : this(null)
        {
        }

        /// <summary>
        /// The byte[] constructor invokes base class UIntBase constructor for 32 bytes
        /// </summary>
        public UInt256(byte[] value)
            : base(32, value)
        {
        }

        /// <summary>
        /// Method Parse receives a big-endian hex string and stores as a UInt256 little-endian 32-bytes array
        /// Example: Parse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff01") should create UInt256 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4
        /// </summary>
        public static new UInt256 Parse(string input)
        {
            if (input == null)
            {
                throw new ArgumentNullException();
            }

            if (input.StartsWith("0x"))
            {
                input = input.Substring(2);
            }

            if (input.Length != 64)
            {
                throw new FormatException();
            }

            return new UInt256(input.HexToBytes().Reverse().ToArray());
        }

        /// <summary>
        /// Method TryParse tries to parse a big-endian hex string and store it as a UInt256 little-endian 32-bytes array
        /// Example: TryParse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff01", result) should create result UInt256 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4
        /// </summary>
        public static bool TryParse(string input, out UInt256 result)
        {
            if (input == null)
            {
                result = null;
                return false;
            }

            if (input.StartsWith("0x"))
            {
                input = input.Substring(2);
            }

            if (input.Length != 64)
            {
                result = null;
                return false;
            }

            var data = new byte[32];
            for (var i = 0; i < 32; i++)
            {
                if (!byte.TryParse(input.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, null, out data[i]))
                {
                    result = null;
                    return false;
                }
            }

            result = new UInt256(data.Reverse().ToArray());
            return true;
        }

        /// <summary>
        /// Operator > returns true if left UInt256 is bigger than right UInt256
        /// Example: UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) > UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) is true
        /// </summary>
        public static bool operator >(UInt256 left, UInt256 right) => left.CompareTo(right) > 0;

        /// <summary>
        /// Operator >= returns true if left UInt256 is bigger or equals to right UInt256
        /// Example: UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) >= UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) is true
        /// </summary>
        public static bool operator >=(UInt256 left, UInt256 right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// Operator < returns true if left UInt256 is less than right UInt256
        /// Example: UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) < UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) is true
        /// </summary>
        public static bool operator <(UInt256 left, UInt256 right) => left.CompareTo(right) < 0;

        /// <summary>
        /// Operator <= returns true if left UInt256 is less or equals to right UInt256
        /// Example: UInt256(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) <= UInt256(01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4) is true
        /// </summary>
        public static bool operator <=(UInt256 left, UInt256 right) => left.CompareTo(right) <= 0;
        
        /// <summary>
        /// Method CompareTo returns 1 if this UInt256 is bigger than other UInt256; -1 if it's smaller; 0 if it's equals
        /// Example: assume this is 01ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a4, this.CompareTo(02ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00a3) returns 1
        /// </summary>
        public int CompareTo(UInt256 other)
        {
            var x = this.ToArray();
            var y = other.ToArray();
            for (var i = x.Length - 1; i >= 0; i--)
            {
                if (x[i] > y[i])
                {
                    return 1;
                }

                if (x[i] < y[i])
                {
                    return -1;
                }
            }

            return 0;
        }

        /// <summary>
        /// Method Equals returns true if objects are equal, false otherwise
        /// </summary>
        bool IEquatable<UInt256>.Equals(UInt256 other) => this.Equals(other);
    }
}
