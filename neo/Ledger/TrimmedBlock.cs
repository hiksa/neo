using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Json;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;

namespace Neo.Ledger
{
    public class TrimmedBlock : BlockBase
    {
        public UInt256[] Hashes;

        private Header header = null;

        public bool IsBlock => this.Hashes.Any();

        public Header Header
        {
            get
            {
                if (this.header == null)
                {
                    this.header = new Header
                    {
                        Version = this.Version,
                        PrevHash = this.PrevHash,
                        MerkleRoot = this.MerkleRoot,
                        Timestamp = this.Timestamp,
                        Index = this.Index,
                        ConsensusData = this.ConsensusData,
                        NextConsensus = this.NextConsensus,
                        Witness = this.Witness
                    };
                }

                return this.header;
            }
        }

        public override int Size => base.Size + Hashes.GetVarSize();

        public Block GetBlock(DataCache<UInt256, TransactionState> cache)
        {
            return new Block
            {
                Version = this.Version,
                PrevHash = this.PrevHash,
                MerkleRoot = this.MerkleRoot,
                Timestamp = this.Timestamp,
                Index = this.Index,
                ConsensusData = this.ConsensusData,
                NextConsensus = this.NextConsensus,
                Witness = this.Witness,
                Transactions = this.Hashes.Select(p => cache[p].Transaction).ToArray()
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.Hashes = reader.ReadSerializableArray<UInt256>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(this.Hashes);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["hashes"] = this.Hashes.Select(p => (JObject)p.ToString()).ToArray();
            return json;
        }
    }
}
