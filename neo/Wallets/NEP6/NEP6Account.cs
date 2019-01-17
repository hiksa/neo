using System;
using Neo.Extensions;
using Neo.IO.Json;

namespace Neo.Wallets.NEP6
{
    internal class NEP6Account : WalletAccount
    {
        private readonly NEP6Wallet wallet;
        private readonly string nep2key;
        private KeyPair key;

        public NEP6Account(NEP6Wallet wallet, UInt160 scriptHash, string nep2key = null)
            : base(scriptHash)
        {
            this.wallet = wallet;
            this.nep2key = nep2key;
        }

        public NEP6Account(NEP6Wallet wallet, UInt160 scriptHash, KeyPair key, string password)
            : this(wallet, scriptHash, key.Export(password, wallet.Scrypt.N, wallet.Scrypt.R, wallet.Scrypt.P))
        {
            this.key = key;
        }

        public JObject Extra { get; set; }

        public bool Decrypted => this.nep2key == null || this.key != null;

        public override bool HasKey => this.nep2key != null;

        public static NEP6Account FromJson(JObject json, NEP6Wallet wallet)
        {
            var scriptHash = json["address"].AsString().ToScriptHash();
            var result = new NEP6Account(wallet, scriptHash, json["key"]?.AsString());
            result.Label = json["label"]?.AsString();
            result.IsDefault = json["isDefault"].AsBoolean();
            result.Lock = json["lock"].AsBoolean();
            result.Contract = NEP6Contract.FromJson(json["contract"]);
            result.Extra = json["extra"];
            return result;
        }

        public override KeyPair GetKey()
        {
            if (this.nep2key == null)
            {
                return null;
            }

            if (this.key == null)
            {
                this.key = this.wallet.DecryptKey(this.nep2key);
            }

            return this.key;
        }

        public KeyPair GetKey(string password)
        {
            if (this.nep2key == null)
            {
                return null;
            }

            if (this.key == null)
            {
                var privateKey = Wallet.GetPrivateKeyFromNEP2(
                    this.nep2key, 
                    password, 
                    this.wallet.Scrypt.N, 
                    this.wallet.Scrypt.R,
                    this.wallet.Scrypt.P);

                this.key = new KeyPair(privateKey);
            }

            return this.key;
        }

        public JObject ToJson()
        {
            var account = new JObject();
            account["address"] = this.ScriptHash.ToAddress();
            account["label"] = this.Label;
            account["isDefault"] = this.IsDefault;
            account["lock"] = this.Lock;
            account["key"] = this.nep2key;
            account["contract"] = ((NEP6Contract)Contract)?.ToJson();
            account["extra"] = this.Extra;
            return account;
        }

        public bool VerifyPassword(string password)
        {
            try
            {
                Wallet.GetPrivateKeyFromNEP2(
                    this.nep2key, 
                    password, 
                    this.wallet.Scrypt.N, 
                    this.wallet.Scrypt.R,
                    this.wallet.Scrypt.P);

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
