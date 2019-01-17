using System;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.Network.P2P.Payloads;

namespace Neo.Consensus
{
    internal class PrepareRequest : ConsensusMessage
    {
        public PrepareRequest()
            : base(ConsensusMessageType.PrepareRequest)
        {
        }

        public override int Size =>
            base.Size + sizeof(ulong) + this.NextConsensus.Size
            + this.TransactionHashes.GetVarSize() + this.MinerTransaction.Size + this.Signature.Length;

        public ulong Nonce { get; set; }

        public UInt160 NextConsensus { get; set; }

        public UInt256[] TransactionHashes { get; set; }

        public MinerTransaction MinerTransaction { get; set; }

        public byte[] Signature { get; set; }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.Nonce = reader.ReadUInt64();
            this.NextConsensus = reader.ReadSerializable<UInt160>();
            this.TransactionHashes = reader.ReadSerializableArray<UInt256>();

            if (this.TransactionHashes.Distinct().Count() != this.TransactionHashes.Length)
            {
                throw new FormatException();
            }

            this.MinerTransaction = reader.ReadSerializable<MinerTransaction>();
            if (this.MinerTransaction.Hash != this.TransactionHashes[0])
            {
                throw new FormatException();
            }

            this.Signature = reader.ReadBytes(64);
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.Nonce);
            writer.Write(this.NextConsensus);
            writer.Write(this.TransactionHashes);
            writer.Write(this.MinerTransaction);
            writer.Write(this.Signature);
        }
    }
}
