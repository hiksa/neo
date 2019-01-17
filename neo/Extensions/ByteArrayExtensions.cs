using Neo.Cryptography;
using Neo.IO;
using Neo.VM;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Neo.Extensions
{
    public static class ByteArrayExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int ToInt32(this byte[] value, int startIndex)
        {
            fixed (byte* pbyte = &value[startIndex])
            {
                return *((int*)pbyte);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe long ToInt64(this byte[] value, int startIndex)
        {
            fixed (byte* pbyte = &value[startIndex])
            {
                return *((long*)pbyte);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ushort ToUInt16(this byte[] value, int startIndex)
        {
            fixed (byte* pbyte = &value[startIndex])
            {
                return *((ushort*)pbyte);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint ToUInt32(this byte[] value, int startIndex)
        {
            fixed (byte* pbyte = &value[startIndex])
            {
                return *(uint*)pbyte;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong ToUInt64(this byte[] value, int startIndex)
        {
            fixed (byte* pbyte = &value[startIndex])
            {
                return *((ulong*)pbyte);
            }
        }

        public static T AsSerializable<T>(this byte[] value, int start = 0) where T : ISerializable, new()
        {
            using (var ms = new MemoryStream(value, start, value.Length - start, false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return reader.ReadSerializable<T>();
            }
        }

        public static ISerializable AsSerializable(this byte[] value, Type type)
        {
            if (!typeof(ISerializable).GetTypeInfo().IsAssignableFrom(type))
            {
                throw new InvalidCastException();
            }

            var serializable = (ISerializable)Activator.CreateInstance(type);
            using (var ms = new MemoryStream(value, false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                serializable.Deserialize(reader);
            }

            return serializable;
        }

        public static T[] AsSerializableArray<T>(this byte[] value, int max = 0x1000000) where T : ISerializable, new()
        {
            using (var ms = new MemoryStream(value, false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return reader.ReadSerializableArray<T>(max);
            }
        }

        public static bool IsMultiSigContract(this byte[] script)
        {
            if (script.Length < 37)
            {
                return false;
            }

            var i = 0;
            if (script[i] > (byte)OpCode.PUSH16)
            {
                return false;
            }

            if (script[i] < (byte)OpCode.PUSH1 && script[i] != 1 && script[i] != 2)
            {
                return false;
            }

            var m = 0;
            switch (script[i])
            {
                case 1:
                    m = script[++i];
                    ++i;
                    break;
                case 2:
                    m = script.ToUInt16(++i);
                    i += 2;
                    break;
                default:
                    m = script[i++] - 80;
                    break;
            }

            if (m < 1 || m > 1024)
            {
                return false;
            }

            var n = 0;
            while (script[i] == 33)
            {
                i += 34;
                if (script.Length <= i)
                {
                    return false;
                }

                ++n;
            }

            if (n < m || n > 1024)
            {
                return false;
            }

            switch (script[i])
            {
                case 1:
                    if (n != script[++i])
                    {
                        return false;
                    }

                    ++i;
                    break;
                case 2:
                    if (script.Length < i + 3 || n != script.ToUInt16(++i))
                    {
                        return false;
                    }

                    i += 2;
                    break;
                default:
                    if (n != script[i++] - 80)
                    {
                        return false;
                    }

                    break;
            }

            if (script[i++] != (byte)OpCode.CHECKMULTISIG)
            {
                return false;
            }

            if (script.Length != i)
            {
                return false;
            }

            return true;
        }

        public static bool IsSignatureContract(this byte[] script)
        {
            if (script.Length != 35 || script[0] != 33 || script[34] != (byte)OpCode.CHECKSIG)
            {
                return false;
            }

            return true;
        }

        public static bool IsStandardContract(this byte[] script) =>
            script.IsSignatureContract() || script.IsMultiSigContract();

        public static UInt160 ToScriptHash(this byte[] script) =>
            new UInt160(Crypto.Default.Hash160(script));
    }
}
