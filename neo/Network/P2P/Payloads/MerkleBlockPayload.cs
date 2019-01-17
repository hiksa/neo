using System.Collections;
using System.IO;
using System.Linq;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class MerkleBlockPayload : BlockBase
    {
        public int TxCount { get; private set; }

        public UInt256[] Hashes { get; private set; }

        public byte[] Flags { get; private set; }

        public override int Size => base.Size + sizeof(int) + this.Hashes.GetVarSize() + this.Flags.GetVarSize();

        public static MerkleBlockPayload Create(Block block, BitArray flags)
        {
            var tree = new MerkleTree(block.Transactions.Select(p => p.Hash).ToArray());
            tree.Trim(flags);

            var buffer = new byte[(flags.Length + 7) / 8];
            flags.CopyTo(buffer, 0);

            return new MerkleBlockPayload
            {
                Version = block.Version,
                PrevHash = block.PrevHash,
                MerkleRoot = block.MerkleRoot,
                Timestamp = block.Timestamp,
                Index = block.Index,
                ConsensusData = block.ConsensusData,
                NextConsensus = block.NextConsensus,
                Witness = block.Witness,
                TxCount = block.Transactions.Length,
                Hashes = tree.ToHashArray(),
                Flags = buffer
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.TxCount = (int)reader.ReadVarInt(int.MaxValue);
            this.Hashes = reader.ReadSerializableArray<UInt256>();
            this.Flags = reader.ReadVarBytes();
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.WriteVarInt(this.TxCount);
            writer.Write(this.Hashes);
            writer.WriteVarBytes(this.Flags);
        }
    }
}
