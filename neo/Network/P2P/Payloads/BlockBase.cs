using System;
using System.IO;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO.Json;
using Neo.Persistence;
using Neo.VM;

namespace Neo.Network.P2P.Payloads
{
    public abstract class BlockBase : IVerifiable
    {
        private UInt256 hash = null;

        public uint Version { get; set; }

        public UInt256 PrevHash { get; set; }

        public UInt256 MerkleRoot { get; set; }

        public uint Timestamp { get; set; }

        public uint Index { get; set; }

        public ulong ConsensusData { get; set; }

        public UInt160 NextConsensus { get; set; }

        public Witness Witness { get; set; }

        public UInt256 Hash
        {
            get
            {
                if (this.hash == null)
                {
                    this.hash = new UInt256(Crypto.Default.Hash256(this.GetHashData()));
                }

                return this.hash;
            }
        }

        Witness[] IVerifiable.Witnesses
        {
            get
            {
                return new[] { this.Witness };
            }

            set
            {
                if (value.Length != 1)
                {
                    throw new ArgumentException();
                }

                this.Witness = value[0];
            }
        }

        public virtual int Size => 
            sizeof(uint) + this.PrevHash.Size + this.MerkleRoot.Size 
            + sizeof(uint) + sizeof(uint) + sizeof(ulong) 
            + this.NextConsensus.Size + 1 + this.Witness.Size;

        public virtual void Deserialize(BinaryReader reader)
        {
            ((IVerifiable)this).DeserializeUnsigned(reader);
            if (reader.ReadByte() != 1)
            {
                throw new FormatException();
            }

            this.Witness = reader.ReadSerializable<Witness>();
        }

        void IVerifiable.DeserializeUnsigned(BinaryReader reader)
        {
            this.Version = reader.ReadUInt32();
            this.PrevHash = reader.ReadSerializable<UInt256>();
            this.MerkleRoot = reader.ReadSerializable<UInt256>();
            this.Timestamp = reader.ReadUInt32();
            this.Index = reader.ReadUInt32();
            this.ConsensusData = reader.ReadUInt64();
            this.NextConsensus = reader.ReadSerializable<UInt160>();
        }

        byte[] IScriptContainer.GetMessage() => this.GetHashData();

        UInt160[] IVerifiable.GetScriptHashesForVerifying(Snapshot snapshot)
        {
            if (this.PrevHash == UInt256.Zero)
            {
                return new UInt160[] { this.Witness.ScriptHash };
            }

            Header previousHeader = snapshot.GetHeader(this.PrevHash);
            if (previousHeader == null)
            {
                throw new InvalidOperationException();
            }

            return new UInt160[] { previousHeader.NextConsensus };
        }

        public virtual void Serialize(BinaryWriter writer)
        {
            ((IVerifiable)this).SerializeUnsigned(writer);
            writer.Write((byte)1);
            writer.Write(this.Witness);
        }

        void IVerifiable.SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write(this.Version);
            writer.Write(this.PrevHash);
            writer.Write(this.MerkleRoot);
            writer.Write(this.Timestamp);
            writer.Write(this.Index);
            writer.Write(this.ConsensusData);
            writer.Write(this.NextConsensus);
        }

        public virtual JObject ToJson()
        {
            var json = new JObject();
            json["hash"] = this.Hash.ToString();
            json["size"] = this.Size;
            json["version"] = this.Version;
            json["previousblockhash"] = this.PrevHash.ToString();
            json["merkleroot"] = this.MerkleRoot.ToString();
            json["time"] = this.Timestamp;
            json["index"] = this.Index;
            json["nonce"] = this.ConsensusData.ToString("x16");
            json["nextconsensus"] = this.NextConsensus.ToAddress();
            json["script"] = this.Witness.ToJson();
            return json;
        }

        public virtual bool Verify(Snapshot snapshot)
        {
            var previousHeader = snapshot.GetHeader(this.PrevHash);
            if (previousHeader == null)
            {
                return false;
            }

            if (previousHeader.Index + 1 != this.Index)
            {
                return false;
            }

            if (previousHeader.Timestamp >= this.Timestamp)
            {
                return false;
            }

            if (!this.VerifyWitnesses(snapshot))
            {
                return false;
            }

            return true;
        }
    }
}
