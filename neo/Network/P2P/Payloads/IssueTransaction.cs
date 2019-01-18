using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Persistence;

namespace Neo.Network.P2P.Payloads
{
    public class IssueTransaction : Transaction
    {
        public IssueTransaction()
            : base(TransactionType.IssueTransaction)
        {
        }

        public override Fixed8 SystemFee
        {
            get
            {
                if (this.Version >= 1)
                {
                    return Fixed8.Zero;
                }

                if (this.Outputs.All(p => p.AssetId == Blockchain.GoverningToken.Hash || p.AssetId == Blockchain.UtilityToken.Hash))
                {
                    return Fixed8.Zero;
                }

                return base.SystemFee;
            }
        }

        public override UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var hashesForVerifying = new HashSet<UInt160>(base.GetScriptHashesForVerifying(snapshot));
            var txResultsForVerifying = this.GetTransactionResults().Where(p => p.Amount < Fixed8.Zero);

            foreach (var result in txResultsForVerifying)
            {
                var asset = snapshot.Assets.TryGet(result.AssetId);
                if (asset == null)
                {
                    throw new InvalidOperationException();
                }

                hashesForVerifying.Add(asset.Issuer);
            }

            return hashesForVerifying.OrderBy(p => p).ToArray();
        }

        public override bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (!base.Verify(snapshot, mempool))
            {
                return false;
            }

            var results = this.GetTransactionResults()
                ?.Where(p => p.Amount < Fixed8.Zero)
                .ToArray();

            if (results == null)
            {
                return false;
            }

            foreach (var result in results)
            {
                var assetState = snapshot.Assets.TryGet(result.AssetId);
                if (assetState == null)
                {
                    return false;
                }

                if (assetState.Amount < Fixed8.Zero)
                {
                    continue;
                }

                var issuedQuantity = mempool
                    .OfType<IssueTransaction>()
                    .Where(p => p != this)
                    .SelectMany(p => p.Outputs)
                    .Where(p => p.AssetId == result.AssetId)
                    .Sum(p => p.Value);

                issuedQuantity += assetState.Available;

                if (assetState.Amount - issuedQuantity < -result.Amount)
                {
                    return false;
                }
            }

            return true;
        }

        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version > 1)
            {
                throw new FormatException();
            }
        }
    }
}
