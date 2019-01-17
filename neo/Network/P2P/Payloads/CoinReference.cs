using System;
using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;

namespace Neo.Network.P2P.Payloads
{
    public class CoinReference : IEquatable<CoinReference>, ISerializable
    {
        public UInt256 PrevHash { get; set; }

        public ushort PrevIndex { get; set; }

        public int Size => this.PrevHash.Size + sizeof(ushort);

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.PrevHash = reader.ReadSerializable<UInt256>();
            this.PrevIndex = reader.ReadUInt16();
        }

        public bool Equals(CoinReference other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return this.PrevHash.Equals(other.PrevHash) && this.PrevIndex.Equals(other.PrevIndex);
        }

        public override bool Equals(object obj)
        {
            if (object.ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is null)
            {
                return false;
            }

            if (!(obj is CoinReference))
            {
                return false;
            }

            return this.Equals((CoinReference)obj);
        }

        public override int GetHashCode()
        {
            return this.PrevHash.GetHashCode() + this.PrevIndex.GetHashCode();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.PrevHash);
            writer.Write(this.PrevIndex);
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["txid"] = this.PrevHash.ToString();
            json["vout"] = this.PrevIndex;
            return json;
        }
    }
}
