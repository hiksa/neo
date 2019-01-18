using System;
using System.IO;
using Neo.Ledger;

namespace Neo.Network.P2P.Payloads
{
    public class Header : BlockBase, IEquatable<Header>
    {
        public override int Size => base.Size + 1;

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            if (reader.ReadByte() != 0)
            {
                throw new FormatException();
            }
        }

        public bool Equals(Header other)
        {
            if (other is null)
            {
                return false;
            }

            if (object.ReferenceEquals(other, this))
            {
                return true;
            }

            return this.Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj) => this.Equals(obj as Header);

        public override int GetHashCode() => this.Hash.GetHashCode();

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write((byte)0);
        }

        public TrimmedBlock Trim()
        {
            return new TrimmedBlock
            {
                Version = this.Version,
                PrevHash = this.PrevHash,
                MerkleRoot = this.MerkleRoot,
                Timestamp = this.Timestamp,
                Index = this.Index,
                ConsensusData = this.ConsensusData,
                NextConsensus = this.NextConsensus,
                Witness = this.Witness,
                Hashes = new UInt256[0]
            };
        }
    }
}
