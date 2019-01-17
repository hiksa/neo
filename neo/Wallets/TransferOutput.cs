using System;
using Neo.Network.P2P.Payloads;

namespace Neo.Wallets
{
    public class TransferOutput
    {
        public bool IsGlobalAsset => this.AssetId.Size == 32;

        public UIntBase AssetId { get; set; }

        public BigDecimal Value { get; set; }

        public UInt160 ScriptHash { get; set; }

        public TransactionOutput ToTxOutput()
        {
            if (this.AssetId is UInt256 assetId)
            {
                return new TransactionOutput
                {
                    AssetId = assetId,
                    Value = this.Value.ToFixed8(),
                    ScriptHash = this.ScriptHash
                };
            }

            throw new NotSupportedException();
        }
    }
}
