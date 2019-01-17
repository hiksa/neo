using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Persistence;

namespace Neo.Network.P2P.Payloads
{
    public class ClaimTransaction : Transaction
    {
        public ClaimTransaction()
            : base(TransactionType.ClaimTransaction)
        {
        }

        public CoinReference[] Claims { get; set; }

        public override Fixed8 NetworkFee => Fixed8.Zero;

        public override int Size => base.Size + this.Claims.GetVarSize();

        public override UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var hashesForVerifying = new HashSet<UInt160>(base.GetScriptHashesForVerifying(snapshot));
            foreach (var group in this.Claims.GroupBy(p => p.PrevHash))
            {
                var tx = snapshot.GetTransaction(group.Key);
                if (tx == null)
                {
                    throw new InvalidOperationException();
                }

                foreach (var claim in group)
                {
                    if (tx.Outputs.Length <= claim.PrevIndex)
                    {
                        throw new InvalidOperationException();
                    }

                    hashesForVerifying.Add(tx.Outputs[claim.PrevIndex].ScriptHash);
                }
            }

            return hashesForVerifying.OrderBy(p => p).ToArray();
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["claims"] = new JArray(this.Claims.Select(p => p.ToJson()).ToArray());
            return json;
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (!base.Verify(snapshot, mempool))
            {
                return false;
            }

            if (this.Claims.Length != this.Claims.Distinct().Count())
            {
                return false;
            }

            if (mempool
                .OfType<ClaimTransaction>()
                .Where(p => p != this)
                .SelectMany(p => p.Claims)
                .Intersect(this.Claims)
                .Any())
            {
                return false;
            }

            var result = this.GetTransactionResults().FirstOrDefault(p => p.AssetId == Blockchain.UtilityToken.Hash);
            if (result == null || result.Amount > Fixed8.Zero)
            {
                return false;
            }

            try
            {
                return snapshot.CalculateBonus(this.Claims, false) == -result.Amount;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }
        
        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version != 0)
            {
                throw new FormatException();
            }

            this.Claims = reader.ReadSerializableArray<CoinReference>();
            if (this.Claims.Length == 0)
            {
                throw new FormatException();
            }
        }

        protected override void SerializeExclusiveData(BinaryWriter writer) => writer.Write(this.Claims);
    }
}
