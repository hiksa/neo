namespace Neo.Wallets.SQLite
{
    internal class UserWalletAccount : WalletAccount
    {
        public UserWalletAccount(UInt160 scriptHash)
            : base(scriptHash)
        {
        }

        public KeyPair Key { get; set; }

        public override bool HasKey => this.Key != null;

        public override KeyPair GetKey() => this.Key;
    }
}
