using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;

namespace Neo.Ledger.States
{
    public class AssetState : StateBase, ICloneable<AssetState>
    {
        public const byte FeeMode = 0;

        public UInt256 AssetId;
        public AssetType Type;
        public string Name;
        public Fixed8 Amount;
        public Fixed8 Available;
        public byte Precision;
        public Fixed8 Fee;
        public UInt160 FeeAddress;
        public ECPoint Owner;
        public UInt160 Admin;
        public UInt160 Issuer;
        public uint Expiration;
        public bool IsFrozen;

        private Dictionary<CultureInfo, string> names;

        public override int Size => 
            base.Size + this.AssetId.Size + sizeof(AssetType) 
            + this.Name.GetVarSize() + this.Amount.Size + this.Available.Size 
            + sizeof(byte) + sizeof(byte) + this.Fee.Size 
            + this.FeeAddress.Size + this.Owner.Size + this.Admin.Size 
            + this.Issuer.Size + sizeof(uint) + sizeof(bool);

        AssetState ICloneable<AssetState>.Clone()
        {
            return new AssetState
            {
                AssetId = this.AssetId,
                Type = this.Type,
                Name = this.Name,
                Amount = this.Amount,
                Available = this.Available,
                Precision = this.Precision,
                Fee = this.Fee,
                FeeAddress = this.FeeAddress,
                Owner = this.Owner,
                Admin = this.Admin,
                Issuer = this.Issuer,
                Expiration = this.Expiration,
                IsFrozen = this.IsFrozen,
                names = this.names
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.AssetId = reader.ReadSerializable<UInt256>();
            this.Type = (AssetType)reader.ReadByte();
            this.Name = reader.ReadVarString();
            this.Amount = reader.ReadSerializable<Fixed8>();
            this.Available = reader.ReadSerializable<Fixed8>();
            this.Precision = reader.ReadByte();            
            reader.ReadByte(); // FeeMode
            this.Fee = reader.ReadSerializable<Fixed8>();
            this.FeeAddress = reader.ReadSerializable<UInt160>();
            this.Owner = ECPoint.DeserializeFrom(reader, ECCurve.Secp256r1);
            this.Admin = reader.ReadSerializable<UInt160>();
            this.Issuer = reader.ReadSerializable<UInt160>();
            this.Expiration = reader.ReadUInt32();
            this.IsFrozen = reader.ReadBoolean();
        }

        void ICloneable<AssetState>.FromReplica(AssetState replica)
        {
            this.AssetId = replica.AssetId;
            this.Type = replica.Type;
            this.Name = replica.Name;
            this.Amount = replica.Amount;
            this.Available = replica.Available;
            this.Precision = replica.Precision;
            ////FeeMode = replica.FeeMode;
            this.Fee = replica.Fee;
            this.FeeAddress = replica.FeeAddress;
            this.Owner = replica.Owner;
            this.Admin = replica.Admin;
            this.Issuer = replica.Issuer;
            this.Expiration = replica.Expiration;
            this.IsFrozen = replica.IsFrozen;
            this.names = replica.names;
        }

        public string GetName(CultureInfo culture = null)
        {
            if (this.Type == AssetType.GoverningToken)
            {
                return "NEO";
            }

            if (this.Type == AssetType.UtilityToken)
            {
                return "NeoGas";
            }

            if (this.names == null)
            {
                JObject nameObject;
                try
                {
                    nameObject = JObject.Parse(this.Name);
                }
                catch (FormatException)
                {
                    nameObject = this.Name;
                }

                if (nameObject is JString)
                {
                    this.names = new Dictionary<CultureInfo, string> { { new CultureInfo("en"), nameObject.AsString() } };
                }
                else
                {
                    this.names = ((JArray)nameObject)
                        .Where(p => p.ContainsProperty("lang") && p.ContainsProperty("name"))
                        .ToDictionary(p => new CultureInfo(p["lang"].AsString()), p => p["name"].AsString());
                }
            }

            if (culture == null)
            {
                culture = CultureInfo.CurrentCulture;
            }

            if (this.names.TryGetValue(culture, out string name))
            {
                return name;
            }
            else if (this.names.TryGetValue(en, out name))
            {
                return name;
            }
            else
            {
                return this.names.Values.First();
            }
        }

        private static readonly CultureInfo en = new CultureInfo("en");

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.AssetId);
            writer.Write((byte)this.Type);
            writer.WriteVarString(this.Name);
            writer.Write(this.Amount);
            writer.Write(this.Available);
            writer.Write(this.Precision);
            writer.Write(FeeMode);
            writer.Write(this.Fee);
            writer.Write(this.FeeAddress);
            writer.Write(this.Owner);
            writer.Write(this.Admin);
            writer.Write(this.Issuer);
            writer.Write(this.Expiration);
            writer.Write(this.IsFrozen);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["id"] = this.AssetId.ToString();
            json["type"] = this.Type;
            try
            {
                json["name"] = this.Name == string.Empty ? null : JObject.Parse(this.Name);
            }
            catch (FormatException)
            {
                json["name"] = this.Name;
            }

            json["amount"] = this.Amount.ToString();
            json["available"] = this.Available.ToString();
            json["precision"] = this.Precision;
            json["owner"] = this.Owner.ToString();
            json["admin"] = this.Admin.ToAddress();
            json["issuer"] = this.Issuer.ToAddress();
            json["expiration"] = this.Expiration;
            json["frozen"] = this.IsFrozen;
            return json;
        }

        public override string ToString() => this.GetName();
    }
}
