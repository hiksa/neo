using System;
using Neo.Network.P2P.Payloads;

namespace Neo.Wallets
{
    public class WalletTransactionEventArgs : EventArgs
    {
        public Transaction Transaction { get; set; }

        public UInt160[] RelatedAccounts { get; set; }

        public uint? Height { get; set; }

        public uint Time { get; set; }
    }
}
