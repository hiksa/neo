using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Persistence;
using Neo.SmartContract;

namespace Neo.Network.P2P.Payloads
{
    [Obsolete]
    public class PublishTransaction : Transaction
    {
        private UInt160 scriptHash;
        
        public PublishTransaction()
            : base(TransactionType.PublishTransaction)
        {
        }

        public byte[] Script { get; private set; }

        public ContractParameterType[] ParameterList { get; private set; }

        public ContractParameterType ReturnType { get; private set; }

        public bool NeedStorage { get; private set; }

        public string Name { get; private set; }

        public string CodeVersion { get; private set; }

        public string Author { get; private set; }

        public string Email { get; private set; }

        public string Description { get; private set; }

        public override int Size =>
            base.Size + this.Script.GetVarSize() + this.ParameterList.GetVarSize()
            + sizeof(ContractParameterType) + this.Name.GetVarSize()
            + this.CodeVersion.GetVarSize() + this.Author.GetVarSize()
            + this.Email.GetVarSize() + this.Description.GetVarSize();

        internal UInt160 ScriptHash
        {
            get
            {
                if (this.scriptHash == null)
                {
                    this.scriptHash = this.Script.ToScriptHash();
                }

                return this.scriptHash;
            }
        }
        
        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["contract"] = new JObject();
            json["contract"]["code"] = new JObject();
            json["contract"]["code"]["hash"] = this.ScriptHash.ToString();
            json["contract"]["code"]["script"] = this.Script.ToHexString();
            json["contract"]["code"]["parameters"] = new JArray(this.ParameterList.Select(p => (JObject)p));
            json["contract"]["code"]["returntype"] = this.ReturnType;
            json["contract"]["needstorage"] = this.NeedStorage;
            json["contract"]["name"] = this.Name;
            json["contract"]["version"] = this.CodeVersion;
            json["contract"]["author"] = this.Author;
            json["contract"]["email"] = this.Email;
            json["contract"]["description"] = this.Description;
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool) => false;

        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version > 1)
            {
                throw new FormatException();
            }

            this.Script = reader.ReadVarBytes();
            this.ParameterList = reader
                .ReadVarBytes()
                .Select(p => (ContractParameterType)p)
                .ToArray();

            this.ReturnType = (ContractParameterType)reader.ReadByte();

            if (this.Version >= 1)
            {
                this.NeedStorage = reader.ReadBoolean();
            }
            else
            {
                this.NeedStorage = false;
            }

            this.Name = reader.ReadVarString(252);
            this.CodeVersion = reader.ReadVarString(252);
            this.Author = reader.ReadVarString(252);
            this.Email = reader.ReadVarString(252);
            this.Description = reader.ReadVarString(65536);
        }

        protected override void SerializeExclusiveData(BinaryWriter writer)
        {
            writer.WriteVarBytes(this.Script);
            writer.WriteVarBytes(this.ParameterList.Cast<byte>().ToArray());
            writer.Write((byte)this.ReturnType);
            if (this.Version >= 1)
            {
                writer.Write(this.NeedStorage);
            }

            writer.WriteVarString(this.Name);
            writer.WriteVarString(this.CodeVersion);
            writer.WriteVarString(this.Author);
            writer.WriteVarString(this.Email);
            writer.WriteVarString(this.Description);
        }
    }
}
