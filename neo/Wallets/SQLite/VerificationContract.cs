using System;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.Wallets.SQLite
{
    public class VerificationContract : SmartContract.Contract, IEquatable<VerificationContract>, ISerializable
    {
        public int Size => 20 + ParameterList.GetVarSize() + Script.GetVarSize();

        public void Deserialize(BinaryReader reader)
        {
            reader.ReadSerializable<UInt160>();
            this.ParameterList = reader
                .ReadVarBytes()
                .Select(p => (ContractParameterType)p)
                .ToArray();

            this.Script = reader.ReadVarBytes();
        }

        public bool Equals(VerificationContract other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return this.ScriptHash.Equals(other.ScriptHash);
        }

        public override bool Equals(object obj) => this.Equals(obj as VerificationContract);

        public override int GetHashCode() => this.ScriptHash.GetHashCode();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(new UInt160());
            writer.WriteVarBytes(this.ParameterList.Select(p => (byte)p).ToArray());
            writer.WriteVarBytes(this.Script);
        }
    }
}
