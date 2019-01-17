using System;

namespace Neo.Wallets.NEP6
{
    internal class WalletLocker : IDisposable
    {
        private NEP6Wallet wallet;

        public WalletLocker(NEP6Wallet wallet)
        {
            this.wallet = wallet;
        }

        public void Dispose() => this.wallet.Lock();        
    }
}
