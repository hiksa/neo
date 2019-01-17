using System;
using System.IO;
using System.Linq;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Ledger
{
    public class StorageKey : IEquatable<StorageKey>, ISerializable
    {
        public UInt160 ScriptHash;
        public byte[] Key;

        int ISerializable.Size => this.ScriptHash.Size + ((this.Key.Length / 16) + 1) * 17;

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.ScriptHash = reader.ReadSerializable<UInt160>();
            this.Key = reader.ReadBytesWithGrouping();
        }

        public bool Equals(StorageKey other)
        {
            if (other is null)
            {
                return false;
            }

            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            return this.ScriptHash.Equals(other.ScriptHash) && this.Key.SequenceEqual(other.Key);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (!(obj is StorageKey))
            {
                return false;
            }

            return this.Equals((StorageKey)obj);
        }

        public override int GetHashCode() =>
            this.ScriptHash.GetHashCode() + (int)this.Key.Murmur32(0);
        
        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.ScriptHash);
            writer.WriteBytesWithGrouping(this.Key);
        }
    }
}
