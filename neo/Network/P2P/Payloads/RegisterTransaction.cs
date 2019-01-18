using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.Wallets;

namespace Neo.Network.P2P.Payloads
{
    [Obsolete]
    public class RegisterTransaction : Transaction
    {
        private UInt160 scriptHash = null;
        
        public RegisterTransaction()
            : base(TransactionType.RegisterTransaction)
        {
        }

        public AssetType AssetType { get; set; }

        public string Name { get; set; }

        public Fixed8 Amount { get; set; }

        public byte Precision { get; set; }

        public ECPoint Owner { get; set; }

        public UInt160 Admin { get; set; }

        public override int Size => 
            base.Size + sizeof(AssetType) + this.Name.GetVarSize() 
            + this.Amount.Size + sizeof(byte) + this.Owner.Size + this.Admin.Size;

        public override Fixed8 SystemFee
        {
            get
            {
                if (this.AssetType == AssetType.GoverningToken || this.AssetType == AssetType.UtilityToken)
                {
                    return Fixed8.Zero;
                }

                return base.SystemFee;
            }
        }

        internal UInt160 OwnerScriptHash
        {
            get
            {
                if (this.scriptHash == null)
                {
                    this.scriptHash = Contract.CreateSignatureRedeemScript(this.Owner).ToScriptHash();
                }

                return this.scriptHash;
            }
        }

        public override UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var owner = Contract.CreateSignatureRedeemScript(this.Owner).ToScriptHash();
            return base.GetScriptHashesForVerifying(snapshot)
                .Union(new[] { owner })
                .OrderBy(p => p)
                .ToArray();
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["asset"] = new JObject();
            json["asset"]["type"] = this.AssetType;
            try
            {
                json["asset"]["name"] = this.Name == string.Empty ? null : JObject.Parse(this.Name);
            }
            catch (FormatException)
            {
                json["asset"]["name"] = this.Name;
            }

            json["asset"]["amount"] = this.Amount.ToString();
            json["asset"]["precision"] = this.Precision;
            json["asset"]["owner"] = this.Owner.ToString();
            json["asset"]["admin"] = this.Admin.ToAddress();
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            return false;
        }
        
        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version != 0)
            {
                throw new FormatException();
            }

            this.AssetType = (AssetType)reader.ReadByte();
            this.Name = reader.ReadVarString(1024);
            this.Amount = reader.ReadSerializable<Fixed8>();
            this.Precision = reader.ReadByte();
            this.Owner = ECPoint.DeserializeFrom(reader, ECCurve.Secp256r1);

            if (this.Owner.IsInfinity 
                && this.AssetType != AssetType.GoverningToken 
                && this.AssetType != AssetType.UtilityToken)
            {
                throw new FormatException();
            }

            this.Admin = reader.ReadSerializable<UInt160>();
        }

        protected override void OnDeserialized()
        {
            base.OnDeserialized();

            if (this.AssetType == AssetType.GoverningToken && !this.Hash.Equals(Blockchain.GoverningToken.Hash))
            {
                throw new FormatException();
            }

            if (this.AssetType == AssetType.UtilityToken && !this.Hash.Equals(Blockchain.UtilityToken.Hash))
            {
                throw new FormatException();
            }
        }

        protected override void SerializeExclusiveData(BinaryWriter writer)
        {
            writer.Write((byte)this.AssetType);
            writer.WriteVarString(this.Name);
            writer.Write(this.Amount);
            writer.Write(this.Precision);
            writer.Write(this.Owner);
            writer.Write(this.Admin);
        }
    }
}
