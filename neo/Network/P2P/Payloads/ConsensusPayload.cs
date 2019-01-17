using System;
using System.IO;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.Network.P2P.Payloads
{
    public class ConsensusPayload : IInventory
    {
        private UInt256 hash = null;

        public uint Version { get; set; }

        public UInt256 PrevHash { get; set; }

        public uint BlockIndex { get; set; }

        public ushort ValidatorIndex { get; set; }

        public uint Timestamp { get; set; }

        public byte[] Data { get; set; }

        public Witness Witness { get; set; }

        UInt256 IInventory.Hash
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

        InventoryType IInventory.InventoryType => InventoryType.Consensus;

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

        public int Size => 
            sizeof(uint) + this.PrevHash.Size + sizeof(uint) 
            + sizeof(ushort) + sizeof(uint) + this.Data.GetVarSize() 
            + 1 + this.Witness.Size;

        void ISerializable.Deserialize(BinaryReader reader)
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
            this.BlockIndex = reader.ReadUInt32();
            this.ValidatorIndex = reader.ReadUInt16();
            this.Timestamp = reader.ReadUInt32();
            this.Data = reader.ReadVarBytes();
        }

        byte[] IScriptContainer.GetMessage() => this.GetHashData();

        UInt160[] IVerifiable.GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var validators = snapshot.GetValidators();
            if (validators.Length <= this.ValidatorIndex)
            {
                throw new InvalidOperationException();
            }

            var validatorHash = Contract
                .CreateSignatureRedeemScript(validators[this.ValidatorIndex])
                .ToScriptHash();

            return new[] { validatorHash };
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            ((IVerifiable)this).SerializeUnsigned(writer);
            writer.Write((byte)1);
            writer.Write(this.Witness);
        }

        void IVerifiable.SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write(this.Version);
            writer.Write(this.PrevHash);
            writer.Write(this.BlockIndex);
            writer.Write(this.ValidatorIndex);
            writer.Write(this.Timestamp);
            writer.WriteVarBytes(this.Data);
        }

        public bool Verify(Snapshot snapshot)
        {
            if (this.BlockIndex <= snapshot.Height)
            {
                return false;
            }

            return this.VerifyWitnesses(snapshot);
        }
    }
}
