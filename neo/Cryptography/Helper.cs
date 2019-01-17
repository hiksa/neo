using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Neo.Cryptography.HashAlgorithms;
using Neo.Extensions;
using Neo.IO;
using Neo.Network.P2P.Payloads;

namespace Neo.Cryptography
{
    public static class Helper
    {
        private static ThreadLocal<SHA256> sha256 = new ThreadLocal<SHA256>(() => SHA256.Create());
        private static ThreadLocal<RIPEMD160Managed> ripemd160 = new ThreadLocal<RIPEMD160Managed>(() => new RIPEMD160Managed());

        internal static byte[] AES256Decrypt(this byte[] block, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(block, 0, block.Length);
                }
            }
        }

        internal static byte[] AES256Encrypt(this byte[] block, byte[] key)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(block, 0, block.Length);
                }
            }
        }

        internal static byte[] AesDecrypt(this byte[] data, byte[] key, byte[] iv)
        {
            if (data == null || key == null || iv == null)
            {
                throw new ArgumentNullException();
            }

            if (data.Length % 16 != 0 || key.Length != 32 || iv.Length != 16)
            {
                throw new ArgumentException();
            }

            using (var aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform decryptor = aes.CreateDecryptor(key, iv))
                {
                    return decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        internal static byte[] AesEncrypt(this byte[] data, byte[] key, byte[] iv)
        {
            if (data == null || key == null || iv == null)
            {
                throw new ArgumentNullException();
            }

            if (data.Length % 16 != 0 || key.Length != 32 || iv.Length != 16)
            {
                throw new ArgumentException();
            }

            using (var aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform encryptor = aes.CreateEncryptor(key, iv))
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        public static byte[] Base58CheckDecode(this string input)
        {
            var buffer = Base58.Decode(input);
            if (buffer.Length < 4)
            {
                throw new FormatException();
            }

            var checksum = buffer.Sha256(0, buffer.Length - 4).Sha256();
            if (!buffer.Skip(buffer.Length - 4).SequenceEqual(checksum.Take(4)))
            {
                throw new FormatException();
            }

            return buffer.Take(buffer.Length - 4).ToArray();
        }

        public static string Base58CheckEncode(this byte[] data)
        {
            var checksum = data.Sha256().Sha256();
            var buffer = new byte[data.Length + 4];

            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            Buffer.BlockCopy(checksum, 0, buffer, data.Length, 4);

            return Base58.Encode(buffer);
        }

        public static byte[] RIPEMD160(this IEnumerable<byte> value) =>
            ripemd160.Value.ComputeHash(value.ToArray());

        public static uint Murmur32(this IEnumerable<byte> value, uint seed)
        {
            using (var murmur = new Murmur3(seed))
            {
                return murmur.ComputeHash(value.ToArray()).ToUInt32(0);
            }
        }

        public static byte[] Sha256(this IEnumerable<byte> value) => 
            sha256.Value.ComputeHash(value.ToArray());

        public static byte[] Sha256(this byte[] value, int offset, int count) =>
            sha256.Value.ComputeHash(value, offset, count);

        internal static bool Test(this BloomFilter filter, Transaction tx)
        {
            if (filter.Check(tx.Hash.ToArray()))
            {
                return true;
            }

            if (tx.Outputs.Any(p => filter.Check(p.ScriptHash.ToArray())))
            {
                return true;
            }

            if (tx.Inputs.Any(p => filter.Check(p.ToArray())))
            {
                return true;
            }

            if (tx.Witnesses.Any(p => filter.Check(p.ScriptHash.ToArray())))
            {
                return true;
            }

#pragma warning disable CS0612
            if (tx is RegisterTransaction registerTransaction && filter.Check(registerTransaction.Admin.ToArray()))
            {
                return true;                
            }
#pragma warning restore CS0612
            return false;
        }

        internal static byte[] ToAesKey(this string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var passwordHashBytes = sha256.ComputeHash(passwordBytes);

                Array.Clear(passwordBytes, 0, passwordBytes.Length);
                Array.Clear(passwordHashBytes, 0, passwordHashBytes.Length);

                return sha256.ComputeHash(passwordHashBytes);
            }
        }

        internal static byte[] ToAesKey(this SecureString password)
        {
            using (var sha256 = SHA256.Create())
            {
                var passwordBytes = password.ToArray();
                var passwordHashBytes = sha256.ComputeHash(passwordBytes);

                Array.Clear(passwordBytes, 0, passwordBytes.Length);
                Array.Clear(passwordHashBytes, 0, passwordHashBytes.Length);

                return sha256.ComputeHash(passwordHashBytes);
            }
        }

        internal static byte[] ToArray(this SecureString s)
        {
            if (s == null)
            {
                throw new NullReferenceException();
            }

            if (s.Length == 0)
            {
                return new byte[0];
            }

            var result = new List<byte>();
            var ptr = SecureStringMarshal.SecureStringToGlobalAllocAnsi(s);
            try
            {
                var i = 0;
                while (true)
                {
                    var b = Marshal.ReadByte(ptr, i++);
                    if (b == 0)
                    {
                        break;
                    }

                    result.Add(b);
                }                
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(ptr);
            }

            return result.ToArray();
        }
    }
}
