using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.SmartContract;

namespace Neo.Ledger.States
{
    public class ContractState : StateBase, ICloneable<ContractState>
    {
        public byte[] Script;
        public ContractParameterType[] ParameterList;
        public ContractParameterType ReturnType;
        public ContractPropertyState ContractProperties;
        public string Name;
        public string CodeVersion;
        public string Author;
        public string Email;
        public string Description;

        private UInt160 scriptHash;

        public bool HasStorage => this.ContractProperties.HasFlag(ContractPropertyState.HasStorage);

        public bool HasDynamicInvoke => this.ContractProperties.HasFlag(ContractPropertyState.HasDynamicInvoke);

        public bool Payable => this.ContractProperties.HasFlag(ContractPropertyState.Payable);

        public UInt160 ScriptHash
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

        public override int Size => 
            base.Size + this.Script.GetVarSize() + sizeof(ContractParameterType) + sizeof(bool) 
            + this.Name.GetVarSize() + this.ParameterList.GetVarSize()
            + this.CodeVersion.GetVarSize() + this.Author.GetVarSize() 
            + this.Email.GetVarSize() + this.Description.GetVarSize();

        ContractState ICloneable<ContractState>.Clone()
        {
            return new ContractState
            {
                Script = this.Script,
                ParameterList = this.ParameterList,
                ReturnType = this.ReturnType,
                ContractProperties = this.ContractProperties,
                Name = this.Name,
                CodeVersion = this.CodeVersion,
                Author = this.Author,
                Email = this.Email,
                Description = this.Description
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.Script = reader.ReadVarBytes();
            this.ParameterList = reader.ReadVarBytes().Select(p => (ContractParameterType)p).ToArray();
            this.ReturnType = (ContractParameterType)reader.ReadByte();
            this.ContractProperties = (ContractPropertyState)reader.ReadByte();
            this.Name = reader.ReadVarString();
            this.CodeVersion = reader.ReadVarString();
            this.Author = reader.ReadVarString();
            this.Email = reader.ReadVarString();
            this.Description = reader.ReadVarString();
        }

        void ICloneable<ContractState>.FromReplica(ContractState replica)
        {
            this.Script = replica.Script;
            this.ParameterList = replica.ParameterList;
            this.ReturnType = replica.ReturnType;
            this.ContractProperties = replica.ContractProperties;
            this.Name = replica.Name;
            this.CodeVersion = replica.CodeVersion;
            this.Author = replica.Author;
            this.Email = replica.Email;
            this.Description = replica.Description;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.WriteVarBytes(this.Script);
            writer.WriteVarBytes(this.ParameterList.Cast<byte>().ToArray());
            writer.Write((byte)this.ReturnType);
            writer.Write((byte)this.ContractProperties);
            writer.WriteVarString(this.Name);
            writer.WriteVarString(this.CodeVersion);
            writer.WriteVarString(this.Author);
            writer.WriteVarString(this.Email);
            writer.WriteVarString(Description);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["hash"] = this.ScriptHash.ToString();
            json["script"] = this.Script.ToHexString();
            json["parameters"] = new JArray(this.ParameterList.Select(p => (JObject)p));
            json["returntype"] = this.ReturnType;
            json["name"] = this.Name;
            json["code_version"] = this.CodeVersion;
            json["author"] = this.Author;
            json["email"] = this.Email;
            json["description"] = this.Description;
            json["properties"] = new JObject();
            json["properties"]["storage"] = this.HasStorage;
            json["properties"]["dynamic_invoke"] = this.HasDynamicInvoke;
            return json;
        }
    }
}
