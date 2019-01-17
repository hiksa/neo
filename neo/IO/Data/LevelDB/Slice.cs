using Neo.Cryptography;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Neo.IO.Data.LevelDB
{
    public struct Slice : IComparable<Slice>, IEquatable<Slice>
    {
        internal byte[] buffer;

        internal Slice(IntPtr data, UIntPtr length)
        {
            this.buffer = new byte[(int)length];

            Marshal.Copy(data, this.buffer, 0, (int)length);
        }

        public int CompareTo(Slice other)
        {
            for (var i = 0; i < this.buffer.Length && i < other.buffer.Length; i++)
            {
                var result = this.buffer[i].CompareTo(other.buffer[i]);
                if (result != 0)
                {
                    return result;
                }
            }

            return this.buffer.Length.CompareTo(other.buffer.Length);
        }

        public bool Equals(Slice other)
        {
            if (this.buffer.Length != other.buffer.Length)
            {
                return false;
            }

            return this.buffer.SequenceEqual(other.buffer);
        }

        public override bool Equals(object obj)
        {
            if (object.ReferenceEquals(null, obj))
            {
                return false;
            }

            if (!(obj is Slice))
            {
                return false;
            }

            return this.Equals((Slice)obj);
        }

        public override int GetHashCode() => (int)this.buffer.Murmur32(0);

        public byte[] ToArray() => this.buffer ?? new byte[0];

        unsafe public bool ToBoolean()
        {
            if (this.buffer.Length != sizeof(bool))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(bool*)pbyte;
            }
        }

        public byte ToByte()
        {
            if (this.buffer.Length != sizeof(byte))
            {
                throw new InvalidCastException();
            }

            return this.buffer[0];
        }

        unsafe public double ToDouble()
        {
            if (this.buffer.Length != sizeof(double))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(double*)pbyte;
            }
        }

        unsafe public short ToInt16()
        {
            if (this.buffer.Length != sizeof(short))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(short*)pbyte;
            }
        }

        unsafe public int ToInt32()
        {
            if (this.buffer.Length != sizeof(int))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(int*)pbyte;
            }
        }

        unsafe public long ToInt64()
        {
            if (this.buffer.Length != sizeof(long))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(long*)pbyte;
            }
        }

        unsafe public float ToSingle()
        {
            if (this.buffer.Length != sizeof(float))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(float*)pbyte;
            }
        }

        public override string ToString() => Encoding.UTF8.GetString(this.buffer);

        unsafe public ushort ToUInt16()
        {
            if (this.buffer.Length != sizeof(ushort))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(ushort*)pbyte;
            }
        }

        unsafe public uint ToUInt32(int index = 0)
        {
            if (this.buffer.Length != sizeof(uint) + index)
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[index])
            {
                return *((uint*)pbyte);
            }
        }

        unsafe public ulong ToUInt64()
        {
            if (this.buffer.Length != sizeof(ulong))
            {
                throw new InvalidCastException();
            }

            fixed (byte* pbyte = &this.buffer[0])
            {
                return *(ulong*)pbyte;
            }
        }

        public static implicit operator Slice(byte[] data) =>
            new Slice { buffer = data };

        public static implicit operator Slice(bool data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };

        public static implicit operator Slice(byte data) =>
            new Slice { buffer = new[] { data } };

        public static implicit operator Slice(double data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };

        public static implicit operator Slice(short data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };

        public static implicit operator Slice(int data) => 
            new Slice { buffer = BitConverter.GetBytes(data) };
        
        public static implicit operator Slice(long data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };

        public static implicit operator Slice(float data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };
        
        public static implicit operator Slice(string data) =>
            new Slice { buffer = Encoding.UTF8.GetBytes(data) };
        
        public static implicit operator Slice(ushort data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };
        
        public static implicit operator Slice(uint data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };
        
        public static implicit operator Slice(ulong data) =>
            new Slice { buffer = BitConverter.GetBytes(data) };
        
        public static bool operator <(Slice x, Slice y) => x.CompareTo(y) < 0;

        public static bool operator <=(Slice x, Slice y) => x.CompareTo(y) <= 0;

        public static bool operator >(Slice x, Slice y) => x.CompareTo(y) > 0;

        public static bool operator >=(Slice x, Slice y) => x.CompareTo(y) >= 0;

        public static bool operator ==(Slice x, Slice y) => x.Equals(y);

        public static bool operator !=(Slice x, Slice y) => !x.Equals(y);
    }
}
