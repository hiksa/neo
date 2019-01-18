using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Neo.Extensions;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using UserWallet = Neo.Wallets.SQLite.UserWallet;

namespace Neo.Wallets.NEP6
{
    public class NEP6Wallet : Wallet
    {
        public readonly ScryptParameters Scrypt;
        private readonly WalletIndexer indexer;
        private readonly string path;
        private readonly Dictionary<UInt160, NEP6Account> accounts;
        private readonly JObject extra;
        private readonly Dictionary<UInt256, Transaction> unconfirmed = new Dictionary<UInt256, Transaction>();

        private string password;
        private string name;
        private Version version;

        public NEP6Wallet(WalletIndexer indexer, string path, string name = null)
        {
            this.indexer = indexer;
            this.path = path;

            if (File.Exists(path))
            {
                JObject wallet;
                using (StreamReader reader = new StreamReader(path))
                {
                    wallet = JObject.Parse(reader);
                }

                this.name = wallet["name"]?.AsString();
                this.version = Version.Parse(wallet["version"].AsString());
                this.Scrypt = ScryptParameters.FromJson(wallet["scrypt"]);
                this.accounts = ((JArray)wallet["accounts"])
                    .Select(p => NEP6Account.FromJson(p, this))
                    .ToDictionary(p => p.ScriptHash);
                this.extra = wallet["extra"];

                this.indexer.RegisterAccounts(this.accounts.Keys);
            }
            else
            {
                this.name = name;
                this.version = Version.Parse("1.0");
                this.Scrypt = ScryptParameters.Default;
                this.accounts = new Dictionary<UInt160, NEP6Account>();
                this.extra = JObject.Null;
            }

            this.indexer.WalletTransaction += this.WalletIndexer_WalletTransaction;
        }

        public override event EventHandler<WalletTransactionEventArgs> WalletTransaction;

        public override string Name => this.name;

        public override Version Version => this.version;

        public override uint WalletHeight => this.indexer.IndexHeight;

        public static NEP6Wallet Migrate(WalletIndexer indexer, string path, string db3path, string password)
        {
            using (var oldWallet = UserWallet.Open(indexer, db3path, password))
            {
                var newWallet = new NEP6Wallet(indexer, path, oldWallet.Name);
                using (newWallet.Unlock(password))
                {
                    foreach (var account in oldWallet.GetAccounts())
                    {
                        newWallet.CreateAccount(account.Contract, account.GetKey());
                    }
                }

                return newWallet;
            }
        }

        public KeyPair DecryptKey(string nep2key) =>
            new KeyPair(GetPrivateKeyFromNEP2(nep2key, this.password, this.Scrypt.N, this.Scrypt.R, this.Scrypt.P));

        public void Save()
        {
            var wallet = new JObject();
            wallet["name"] = this.name;
            wallet["version"] = this.version.ToString();
            wallet["scrypt"] = this.Scrypt.ToJson();
            wallet["accounts"] = new JArray(this.accounts.Values.Select(p => p.ToJson()));
            wallet["extra"] = this.extra;

            File.WriteAllText(this.path, wallet.ToString());
        }

        public IDisposable Unlock(string password)
        {
            if (!this.VerifyPassword(password))
            {
                throw new CryptographicException();
            }

            this.password = password;
            return new WalletLocker(this);
        }

        public override void ApplyTransaction(Transaction tx)
        {
            lock (this.unconfirmed)
            {
                this.unconfirmed[tx.Hash] = tx;
            }

            var transactionEventArgs = new WalletTransactionEventArgs
            {
                Transaction = tx,
                RelatedAccounts = tx.Witnesses
                    .Select(p => p.ScriptHash)
                    .Union(tx.Outputs.Select(p => p.ScriptHash))
                    .Where(p => this.Contains(p))
                    .ToArray(),
                Height = null,
                Time = DateTime.UtcNow.ToTimestamp()
            };

            this.WalletTransaction?.Invoke(this, transactionEventArgs);
        }

        public override bool Contains(UInt160 scriptHash)
        {
            lock (this.accounts)
            {
                return this.accounts.ContainsKey(scriptHash);
            }
        }

        public override WalletAccount CreateAccount(byte[] privateKey)
        {
            var key = new KeyPair(privateKey);
            var contract = new NEP6Contract
            {
                Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                ParameterList = new[] { ContractParameterType.Signature },
                ParameterNames = new[] { "signature" },
                Deployed = false
            };

            var account = new NEP6Account(this, contract.ScriptHash, key, this.password)
            {
                Contract = contract
            };

            this.AddAccount(account, false);
            return account;
        }

        public override WalletAccount CreateAccount(Contract contract, KeyPair key = null)
        {
            var nep6contract = contract as NEP6Contract;
            if (nep6contract == null)
            {
                nep6contract = new NEP6Contract
                {
                    Script = contract.Script,
                    ParameterList = contract.ParameterList,
                    ParameterNames = contract.ParameterList.Select((p, i) => $"parameter{i}").ToArray(),
                    Deployed = false
                };
            }

            NEP6Account account;
            if (key == null)
            {
                account = new NEP6Account(this, nep6contract.ScriptHash);
            }
            else
            {
                account = new NEP6Account(this, nep6contract.ScriptHash, key, this.password);
            }

            account.Contract = nep6contract;
            this.AddAccount(account, false);
            return account;
        }

        public override WalletAccount CreateAccount(UInt160 scriptHash)
        {
            var account = new NEP6Account(this, scriptHash);
            this.AddAccount(account, true);
            return account;
        }

        public override bool DeleteAccount(UInt160 scriptHash)
        {
            bool removed;
            lock (this.accounts)
            {
                removed = this.accounts.Remove(scriptHash);
            }

            if (removed)
            {
                this.indexer.UnregisterAccounts(new[] { scriptHash });
            }

            return removed;
        }

        public override void Dispose() =>
            this.indexer.WalletTransaction -= this.WalletIndexer_WalletTransaction;

        public override Coin[] FindUnspentCoins(UInt256 assetId, Fixed8 amount, UInt160[] from)
        {
            var allUnspentCoins = this.FindUnspentCoins(from);
            var unspents = allUnspentCoins
                .ToArray()
                .Where(coin => this.GetAccount(coin.Output.ScriptHash).Contract.Script.IsSignatureContract());

            return FindUnspentCoins(unspents, assetId, amount) ?? base.FindUnspentCoins(assetId, amount, from);
        }

        public override WalletAccount GetAccount(UInt160 scriptHash)
        {
            lock (this.accounts)
            {
                this.accounts.TryGetValue(scriptHash, out NEP6Account account);
                return account;
            }
        }

        public override IEnumerable<WalletAccount> GetAccounts()
        {
            lock (this.accounts)
            {
                foreach (NEP6Account account in this.accounts.Values)
                {
                    yield return account;
                }
            }
        }

        public override IEnumerable<Coin> GetCoins(IEnumerable<UInt160> accounts)
        {
            if (this.unconfirmed.Count == 0)
            {
                return this.indexer.GetCoins(accounts);
            }
            else
            {
                return GetCoinsInternal();
            }

            IEnumerable<Coin> GetCoinsInternal()
            {
                HashSet<CoinReference> inputs, claims;
                Coin[] unconfirmedCoins;
                lock (this.unconfirmed)
                {
                    inputs = new HashSet<CoinReference>(this.unconfirmed.Values.SelectMany(p => p.Inputs));
                    claims = new HashSet<CoinReference>(this.unconfirmed.Values.OfType<ClaimTransaction>().SelectMany(p => p.Claims));
                    unconfirmedCoins = this.unconfirmed
                        .Values
                        .SelectMany(tx => tx.Outputs.Select((o, i) => new Coin
                        {
                            Reference = new CoinReference
                            {
                                PrevHash = tx.Hash,
                                PrevIndex = (ushort)i
                            },
                            Output = o,
                            State = CoinStates.Unconfirmed
                        }))
                        .ToArray();
                }

                foreach (var coin in this.indexer.GetCoins(accounts))
                {
                    if (inputs.Contains(coin.Reference))
                    {
                        if (coin.Output.AssetId.Equals(Blockchain.GoverningToken.Hash))
                        {
                            yield return new Coin
                            {
                                Reference = coin.Reference,
                                Output = coin.Output,
                                State = coin.State | CoinStates.Spent
                            };
                        }

                        continue;
                    }
                    else if (claims.Contains(coin.Reference))
                    {
                        continue;
                    }

                    yield return coin;
                }

                var accounts_set = new HashSet<UInt160>(accounts);
                foreach (var coin in unconfirmedCoins)
                {
                    if (accounts_set.Contains(coin.Output.ScriptHash))
                    {
                        yield return coin;
                    }
                }
            }
        }

        public override IEnumerable<UInt256> GetTransactions()
        {
            foreach (var hash in this.indexer.GetTransactions(this.accounts.Keys))
            {
                yield return hash;
            }

            lock (this.unconfirmed)
            {
                foreach (var hash in this.unconfirmed.Keys)
                {
                    yield return hash;
                }
            }
        }

        public override WalletAccount Import(X509Certificate2 cert)
        {
            KeyPair key;
            using (var ecdsa = cert.GetECDsaPrivateKey())
            {
                key = new KeyPair(ecdsa.ExportParameters(true).D);
            }

            var contract = new NEP6Contract
            {
                Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                ParameterList = new[] { ContractParameterType.Signature },
                ParameterNames = new[] { "signature" },
                Deployed = false
            };

            var account = new NEP6Account(this, contract.ScriptHash, key, this.password)
            {
                Contract = contract
            };

            this.AddAccount(account, true);
            return account;
        }

        public override WalletAccount Import(string wif)
        {
            var key = new KeyPair(GetPrivateKeyFromWIF(wif));
            var contract = new NEP6Contract
            {
                Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                ParameterList = new[] { ContractParameterType.Signature },
                ParameterNames = new[] { "signature" },
                Deployed = false
            };

            var account = new NEP6Account(this, contract.ScriptHash, key, this.password)
            {
                Contract = contract
            };

            this.AddAccount(account, true);
            return account;
        }

        public override WalletAccount Import(string nep2, string passphrase)
        {
            var key = new KeyPair(GetPrivateKeyFromNEP2(nep2, passphrase));
            var contract = new NEP6Contract
            {
                Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                ParameterList = new[] { ContractParameterType.Signature },
                ParameterNames = new[] { "signature" },
                Deployed = false
            };

            NEP6Account account;
            if (this.Scrypt.N == 16384 && this.Scrypt.R == 8 && this.Scrypt.P == 8)
            {
                account = new NEP6Account(this, contract.ScriptHash, nep2);
            }
            else
            {
                account = new NEP6Account(this, contract.ScriptHash, key, passphrase);
            }

            account.Contract = contract;
            this.AddAccount(account, true);

            return account;
        }

        public override bool VerifyPassword(string password)
        {
            lock (this.accounts)
            {
                var account = this.accounts.Values.FirstOrDefault(p => !p.Decrypted);
                if (account == null)
                {
                    account = this.accounts.Values.FirstOrDefault(p => p.HasKey);
                }

                if (account == null)
                {
                    return true;
                }

                if (account.Decrypted)
                {
                    return account.VerifyPassword(password);
                }
                else
                {
                    try
                    {
                        account.GetKey(password);
                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }
            }
        }

        internal void Lock() => this.password = null;

        private void AddAccount(NEP6Account account, bool isImporting)
        {
            lock (this.accounts)
            {
                if (this.accounts.TryGetValue(account.ScriptHash, out NEP6Account existingAccount))
                {
                    account.Label = existingAccount.Label;
                    account.IsDefault = existingAccount.IsDefault;
                    account.Lock = existingAccount.Lock;

                    if (account.Contract == null)
                    {
                        account.Contract = existingAccount.Contract;
                    }
                    else
                    {
                        var exeistingContract = (NEP6Contract)existingAccount.Contract;
                        if (exeistingContract != null)
                        {
                            var contract = (NEP6Contract)account.Contract;
                            contract.ParameterNames = exeistingContract.ParameterNames;
                            contract.Deployed = exeistingContract.Deployed;
                        }
                    }

                    account.Extra = existingAccount.Extra;
                }
                else
                {
                    var height = isImporting ? 0 : Blockchain.Instance.Height;
                    this.indexer.RegisterAccounts(new[] { account.ScriptHash }, height);
                }

                this.accounts[account.ScriptHash] = account;
            }
        }

        private void WalletIndexer_WalletTransaction(object sender, WalletTransactionEventArgs e)
        {
            lock (this.unconfirmed)
            {
                this.unconfirmed.Remove(e.Transaction.Hash);
            }

            UInt160[] relatedAccounts;
            lock (this.accounts)
            {
                relatedAccounts = e.RelatedAccounts.Where(p => this.accounts.ContainsKey(p)).ToArray();
            }

            if (relatedAccounts.Length > 0)
            {
                var walletTransactionEventArgs = new WalletTransactionEventArgs
                {
                    Transaction = e.Transaction,
                    RelatedAccounts = relatedAccounts,
                    Height = e.Height,
                    Time = e.Time
                };

                this.WalletTransaction?.Invoke(this, walletTransactionEventArgs);
            }
        }
    }
}
