using Neo.Extensions;
using Neo.SmartContract;

namespace Neo.Wallets
{
    public abstract class WalletAccount
    {
        protected WalletAccount(UInt160 scriptHash)
        {
            this.ScriptHash = scriptHash;
        }

        public abstract bool HasKey { get; }

        public string Address => this.ScriptHash.ToAddress();

        public bool WatchOnly => this.Contract == null;

        public UInt160 ScriptHash { get; private set; }

        public string Label { get; set; }

        public bool IsDefault { get; set; }

        public bool Lock { get; set; }

        public Contract Contract { get; set; }

        public abstract KeyPair GetKey();
    }
}
