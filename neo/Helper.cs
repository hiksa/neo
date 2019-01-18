using System;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;

namespace Neo
{
    public static class Helper
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime ToDateTime(this uint timestamp) =>
            UnixEpoch.AddSeconds(timestamp).ToLocalTime();

        public static DateTime ToDateTime(this ulong timestamp) =>
            UnixEpoch.AddSeconds(timestamp).ToLocalTime();

        public static uint ToTimestamp(this DateTime time) =>
            (uint)(time.ToUniversalTime() - UnixEpoch).TotalSeconds;

        internal static BigInteger NextBigInteger(this Random rand, int sizeInBits)
        {
            if (sizeInBits < 0)
            {
                throw new ArgumentException("sizeInBits must be non-negative");
            }

            if (sizeInBits == 0)
            {
                return 0;
            }

            var bytes = new byte[(sizeInBits / 8) + 1];
            rand.NextBytes(bytes);

            if (sizeInBits % 8 == 0)
            {
                bytes[bytes.Length - 1] = 0;
            }
            else
            {
                bytes[bytes.Length - 1] &= (byte)((1 << (sizeInBits % 8)) - 1);
            }

            return new BigInteger(bytes);
        }

        internal static BigInteger NextBigInteger(this RandomNumberGenerator rng, int sizeInBits)
        {
            if (sizeInBits < 0)
            {
                throw new ArgumentException("sizeInBits must be non-negative");
            }

            if (sizeInBits == 0)
            {
                return 0;
            }

            var bytes = new byte[(sizeInBits / 8) + 1];
            rng.GetBytes(bytes);

            if (sizeInBits % 8 == 0)
            {
                bytes[bytes.Length - 1] = 0;
            }
            else
            {
                bytes[bytes.Length - 1] &= (byte)((1 << (sizeInBits % 8)) - 1);
            }

            return new BigInteger(bytes);
        }    

        internal static IPAddress Unmap(this IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return address;
        }

        internal static IPEndPoint Unmap(this IPEndPoint endPoint)
        {
            if (!endPoint.Address.IsIPv4MappedToIPv6)
            {
                return endPoint;
            }

            return new IPEndPoint(endPoint.Address.Unmap(), endPoint.Port);
        }
    }
}
