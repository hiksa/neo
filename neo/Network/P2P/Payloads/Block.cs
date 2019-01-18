using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;

namespace Neo.Network.P2P.Payloads
{
    public class Block : BlockBase, IInventory, IEquatable<Block>
    {
        private Header header = null;

        public Transaction[] Transactions { get; set; }

        public Header Header
        {
            get
            {
                if (this.header == null)
                {
                    this.header = new Header
                    {
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

        InventoryType IInventory.InventoryType => InventoryType.Block;

        public override int Size => base.Size + this.Transactions.GetVarSize();

        public static Fixed8 CalculateNetFee(IEnumerable<Transaction> transactions)
        {
            var ts = transactions
                .Where(p => p.Type.IsNoneOf(TransactionType.MinerTransaction, TransactionType.ClaimTransaction))
                .ToArray();

            var amountIn = ts
                .SelectMany(p => p.References.Values.Where(o => o.AssetId == Blockchain.UtilityToken.Hash))
                .Sum(p => p.Value);

            var amountOut = ts
                .SelectMany(p => p.Outputs.Where(o => o.AssetId == Blockchain.UtilityToken.Hash))
                .Sum(p => p.Value);

            var systemFee = ts.Sum(p => p.SystemFee);
            var networkFee = amountIn - amountOut - systemFee;

            return networkFee;
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.Transactions = new Transaction[reader.ReadVarInt(0x10000)];

            if (this.Transactions.Length == 0)
            {
                throw new FormatException();
            }

            var hashes = new HashSet<UInt256>();
            for (int i = 0; i < this.Transactions.Length; i++)
            {
                this.Transactions[i] = Transaction.DeserializeFrom(reader);
                if (i == 0)
                {
                    if (this.Transactions[0].Type != TransactionType.MinerTransaction)
                    {
                        throw new FormatException();
                    }
                }
                else
                {
                    if (this.Transactions[i].Type == TransactionType.MinerTransaction)
                    {
                        throw new FormatException();
                    }
                }

                if (!hashes.Add(this.Transactions[i].Hash))
                {
                    throw new FormatException();
                }
            }

            if (MerkleTree.ComputeRoot(this.Transactions.Select(p => p.Hash).ToArray()) != this.MerkleRoot)
            {
                throw new FormatException();
            }
        }

        public bool Equals(Block other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return this.Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj) => this.Equals(obj as Block);
        
        public override int GetHashCode() => this.Hash.GetHashCode();
        
        public void RebuildMerkleRoot() =>
            this.MerkleRoot = MerkleTree.ComputeRoot(this.Transactions.Select(p => p.Hash).ToArray());
        
        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.Transactions);
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["tx"] = this.Transactions.Select(p => p.ToJson()).ToArray();
            return json;
        }

        public TrimmedBlock Trim() => 
            new TrimmedBlock
            {
                Version = this.Version,
                PrevHash = this.PrevHash,
                MerkleRoot = this.MerkleRoot,
                Timestamp = this.Timestamp,
                Index = this.Index,
                ConsensusData = this.ConsensusData,
                NextConsensus = this.NextConsensus,
                Witness = this.Witness,
                Hashes = this.Transactions.Select(p => p.Hash).ToArray()
            };
    }
}
