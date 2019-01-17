using System;
using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Wallets;

namespace Neo.Network.P2P.Payloads
{
    public class TransactionOutput : ISerializable
    {
        public UInt256 AssetId { get; set; }

        public Fixed8 Value { get; set; }

        public UInt160 ScriptHash { get; set; }

        public int Size => this.AssetId.Size + this.Value.Size + this.ScriptHash.Size;

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.AssetId = reader.ReadSerializable<UInt256>();
            this.Value = reader.ReadSerializable<Fixed8>();

            if (this.Value <= Fixed8.Zero)
            {
                throw new FormatException();
            }

            this.ScriptHash = reader.ReadSerializable<UInt160>();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(this.AssetId);
            writer.Write(this.Value);
            writer.Write(this.ScriptHash);
        }

        public JObject ToJson(ushort index)
        {
            var json = new JObject();
            json["n"] = index;
            json["asset"] = this.AssetId.ToString();
            json["value"] = this.Value.ToString();
            json["address"] = this.ScriptHash.ToAddress();
            return json;
        }
    }
}
