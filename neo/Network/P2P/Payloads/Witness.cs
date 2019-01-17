using System.IO;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.VM;

namespace Neo.Network.P2P.Payloads
{
    public class Witness : ISerializable
    {
        private UInt160 scriptHash;

        public byte[] InvocationScript { get; set; }

        public byte[] VerificationScript { get; set; }

        public virtual UInt160 ScriptHash
        {
            get
            {
                if (this.scriptHash == null)
                {
                    this.scriptHash = this.VerificationScript.ToScriptHash();
                }

                return this.scriptHash;
            }
        }

        public int Size => this.InvocationScript.GetVarSize() + this.VerificationScript.GetVarSize();

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.InvocationScript = reader.ReadVarBytes(65536);
            this.VerificationScript = reader.ReadVarBytes(65536);
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.WriteVarBytes(this.InvocationScript);
            writer.WriteVarBytes(this.VerificationScript);
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["invocation"] = this.InvocationScript.ToHexString();
            json["verification"] = this.VerificationScript.ToHexString();
            return json;
        }
    }
}
