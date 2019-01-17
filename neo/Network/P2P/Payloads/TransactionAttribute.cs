using System;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.VM;

namespace Neo.Network.P2P.Payloads
{
    public class TransactionAttribute : ISerializable
    {
        public TransactionAttributeUsage Usage { get; set; }

        public byte[] Data { get; set; }

        public int Size
        {
            get
            {
                if (this.Usage == TransactionAttributeUsage.ContractHash 
                    || this.Usage == TransactionAttributeUsage.ECDH02 
                    || this.Usage == TransactionAttributeUsage.ECDH03 
                    || this.Usage == TransactionAttributeUsage.Vote 
                    || (this.Usage >= TransactionAttributeUsage.Hash1 && this.Usage <= TransactionAttributeUsage.Hash15))
                {
                    return sizeof(TransactionAttributeUsage) + 32;
                }
                else if (this.Usage == TransactionAttributeUsage.Script)
                {
                    return sizeof(TransactionAttributeUsage) + 20;
                }
                else if (this.Usage == TransactionAttributeUsage.DescriptionUrl)
                {
                    return sizeof(TransactionAttributeUsage) + sizeof(byte) + this.Data.Length;
                }
                else
                {
                    return sizeof(TransactionAttributeUsage) + this.Data.GetVarSize();
                }
            }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Usage = (TransactionAttributeUsage)reader.ReadByte();

            if (this.Usage == TransactionAttributeUsage.ContractHash 
                || this.Usage == TransactionAttributeUsage.Vote 
                || (this.Usage >= TransactionAttributeUsage.Hash1 && this.Usage <= TransactionAttributeUsage.Hash15))
            {
                this.Data = reader.ReadBytes(32);
            }
            else if (this.Usage == TransactionAttributeUsage.ECDH02 
                || this.Usage == TransactionAttributeUsage.ECDH03)
            {
                this.Data = new[] { (byte)this.Usage }.Concat(reader.ReadBytes(32)).ToArray();
            }
            else if (this.Usage == TransactionAttributeUsage.Script)
            {
                this.Data = reader.ReadBytes(20);
            }
            else if (this.Usage == TransactionAttributeUsage.DescriptionUrl)
            {
                this.Data = reader.ReadBytes(reader.ReadByte());
            }
            else if (this.Usage == TransactionAttributeUsage.Description 
                || this.Usage >= TransactionAttributeUsage.Remark)
            {
                this.Data = reader.ReadVarBytes(ushort.MaxValue);
            }
            else
            {
                throw new FormatException();
            }
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write((byte)this.Usage);
            if (this.Usage == TransactionAttributeUsage.DescriptionUrl)
            {
                writer.Write((byte)this.Data.Length);
            }
            else if (this.Usage == TransactionAttributeUsage.Description 
                || this.Usage >= TransactionAttributeUsage.Remark)
            {
                writer.WriteVarInt(this.Data.Length);
            }

            if (this.Usage == TransactionAttributeUsage.ECDH02 
                || this.Usage == TransactionAttributeUsage.ECDH03)
            {
                writer.Write(this.Data, 1, 32);
            }
            else
            {
                writer.Write(this.Data);
            }
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["usage"] = this.Usage;
            json["data"] = this.Data.ToHexString();
            return json;
        }
    }
}
