using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;

namespace Neo.Wallets.SQLite
{
    public class UserWallet : Wallet
    {
        private readonly object lockObject = new object();
        private readonly WalletIndexer indexer;
        private readonly string path;
        private readonly byte[] iv;
        private readonly byte[] masterKey;
        private readonly Dictionary<UInt160, UserWalletAccount> accounts;
        private readonly Dictionary<UInt256, Transaction> unconfirmed = new Dictionary<UInt256, Transaction>();
        
        private UserWallet(WalletIndexer indexer, string path, byte[] passwordKey, bool create)
        {
            this.indexer = indexer;
            this.path = path;
            if (create)
            {
                this.iv = new byte[16];
                this.masterKey = new byte[32];
                this.accounts = new Dictionary<UInt160, UserWalletAccount>();
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(this.iv);
                    rng.GetBytes(this.masterKey);
                }

                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

                this.BuildDatabase();

                this.SaveStoredData("PasswordHash", passwordKey.Sha256());
                this.SaveStoredData("IV", this.iv);

                var masterKey = this.masterKey.AesEncrypt(passwordKey, this.iv);
                this.SaveStoredData("MasterKey", masterKey);

                var version = new[] 
                    {
                        assemblyVersion.Major,
                        assemblyVersion.Minor,
                        assemblyVersion.Build,
                        assemblyVersion.Revision
                    }
                    .Select(p => BitConverter.GetBytes(p))
                    .SelectMany(p => p)
                    .ToArray();
                this.SaveStoredData("Version", version);
            }
            else
            {
                var passwordHash = this.LoadStoredData("PasswordHash");
                if (passwordHash != null && !passwordHash.SequenceEqual(passwordKey.Sha256()))
                {
                    throw new CryptographicException();
                }

                this.iv = this.LoadStoredData("IV");
                this.masterKey = this.LoadStoredData("MasterKey").AesDecrypt(passwordKey, this.iv);
                this.accounts = this.LoadAccounts();
                this.indexer.RegisterAccounts(this.accounts.Keys);
            }

            this.indexer.WalletTransaction += this.WalletIndexerWalletTransaction;
        }

        public override event EventHandler<WalletTransactionEventArgs> WalletTransaction;

        public override string Name => Path.GetFileNameWithoutExtension(this.path);

        public override uint WalletHeight => this.indexer.IndexHeight;

        public override Version Version
        {
            get
            {
                var buffer = this.LoadStoredData("Version");
                if (buffer == null || buffer.Length < 16)
                {
                    return new Version(0, 0);
                }

                var major = buffer.ToInt32(0);
                var minor = buffer.ToInt32(4);
                var build = buffer.ToInt32(8);
                var revision = buffer.ToInt32(12);

                return new Version(major, minor, build, revision);
            }
        }

        public static UserWallet Create(WalletIndexer indexer, string path, string password) =>
            new UserWallet(indexer, path, password.ToAesKey(), true);

        public static UserWallet Create(WalletIndexer indexer, string path, SecureString password) =>
            new UserWallet(indexer, path, password.ToAesKey(), true);

        public static UserWallet Open(WalletIndexer indexer, string path, string password) =>
            new UserWallet(indexer, path, password.ToAesKey(), false);

        public static UserWallet Open(WalletIndexer indexer, string path, SecureString password) =>
            new UserWallet(indexer, path, password.ToAesKey(), false);

        public bool ChangePassword(string oldPassword, string newPassword)
        {
            if (!this.VerifyPassword(oldPassword))
            {
                return false;
            }

            var passwordKey = newPassword.ToAesKey();
            try
            {
                this.SaveStoredData("PasswordHash", passwordKey.Sha256());
                this.SaveStoredData("MasterKey", this.masterKey.AesEncrypt(passwordKey, this.iv));
                return true;
            }
            finally
            {
                Array.Clear(passwordKey, 0, passwordKey.Length);
            }
        }

        public override void ApplyTransaction(Transaction tx)
        {
            lock (this.unconfirmed)
            {
                this.unconfirmed[tx.Hash] = tx;
            }

            var relatedAccounts = tx.Witnesses
                .Select(p => p.ScriptHash)
                .Union(tx.Outputs.Select(p => p.ScriptHash))
                .Where(p => Contains(p))
                .ToArray();

            var walletTransactionEventArgs = new WalletTransactionEventArgs
            {
                Transaction = tx,
                RelatedAccounts = relatedAccounts,
                Height = null,
                Time = DateTime.UtcNow.ToTimestamp()
            };

            this.WalletTransaction?.Invoke(this, walletTransactionEventArgs);
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
            var contract = new VerificationContract
            {
                Script = SmartContract.Contract.CreateSignatureRedeemScript(key.PublicKey),
                ParameterList = new[] { ContractParameterType.Signature }
            };

            var account = new UserWalletAccount(contract.ScriptHash)
            {
                Key = key,
                Contract = contract
            };

            this.AddAccount(account, false);

            return account;
        }

        public override WalletAccount CreateAccount(SmartContract.Contract contract, KeyPair key = null)
        {
            var verificationContract = contract as VerificationContract;
            if (verificationContract == null)
            {
                verificationContract = new VerificationContract
                {
                    Script = contract.Script,
                    ParameterList = contract.ParameterList
                };
            }

            var account = new UserWalletAccount(verificationContract.ScriptHash)
            {
                Key = key,
                Contract = verificationContract
            };

            this.AddAccount(account, false);
            return account;
        }

        public override WalletAccount CreateAccount(UInt160 scriptHash)
        {
            var account = new UserWalletAccount(scriptHash);
            this.AddAccount(account, true);
            return account;
        }

        public override bool DeleteAccount(UInt160 scriptHash)
        {
            UserWalletAccount account;
            lock (this.accounts)
            {
                if (this.accounts.TryGetValue(scriptHash, out account))
                {
                    this.accounts.Remove(scriptHash);
                }
            }

            if (account != null)
            {
                this.indexer.UnregisterAccounts(new[] { scriptHash });
                lock (this.lockObject)
                {
                    using (var ctx = new WalletDataContext(this.path))
                    {
                        if (account.HasKey)
                        {
                            var existingAccount = ctx.Accounts
                                .First(p => p.PublicKeyHash.SequenceEqual(account.Key.PublicKeyHash.ToArray()));

                            ctx.Accounts.Remove(existingAccount);
                        }

                        if (account.Contract != null)
                        {
                            var existingContract = ctx.Contracts
                                .First(p => p.ScriptHash.SequenceEqual(scriptHash.ToArray()));

                            ctx.Contracts.Remove(existingContract);
                        }
                        
                        var existingAddress = ctx.Addresses
                            .First(p => p.ScriptHash.SequenceEqual(scriptHash.ToArray()));

                        ctx.Addresses.Remove(existingAddress);
                        ctx.SaveChanges();
                    }
                }

                return true;
            }

            return false;
        }

        public override void Dispose() => 
            this.indexer.WalletTransaction -= this.WalletIndexerWalletTransaction;

        public override Coin[] FindUnspentCoins(UInt256 assetId, Fixed8 amount, UInt160[] from)
        {
            var unspentCoints = this.FindUnspentCoins(from)
                .ToArray()
                .Where(p => this.GetAccount(p.Output.ScriptHash).Contract.Script.IsSignatureContract());

            return 
                Wallet.FindUnspentCoins(unspentCoints, assetId, amount) 
                ?? base.FindUnspentCoins(assetId, amount, from);
        }

        public override WalletAccount GetAccount(UInt160 scriptHash)
        {
            lock (this.accounts)
            {
                this.accounts.TryGetValue(scriptHash, out UserWalletAccount account);
                return account;
            }
        }

        public override IEnumerable<WalletAccount> GetAccounts()
        {
            lock (this.accounts)
            {
                foreach (var account in this.accounts.Values)
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
                        .Select(tx => tx.Outputs.Select((o, i) => new Coin
                        {
                            Reference = new CoinReference
                            {
                                PrevHash = tx.Hash,
                                PrevIndex = (ushort)i
                            },
                            Output = o,
                            State = CoinStates.Unconfirmed
                        }))
                        .SelectMany(p => p)
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

                var distinctAccounts = new HashSet<UInt160>(accounts);
                foreach (var coin in unconfirmedCoins)
                {
                    if (distinctAccounts.Contains(coin.Output.ScriptHash))
                    {
                        yield return coin;
                    }
                }
            }
        }

        public override IEnumerable<UInt256> GetTransactions()
        {
            var allTransactions = this.indexer.GetTransactions(this.accounts.Keys);
            foreach (var hash in allTransactions)
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

        public override bool VerifyPassword(string password) =>
            password.ToAesKey().Sha256().SequenceEqual(this.LoadStoredData("PasswordHash"));

        private static void SaveStoredData(WalletDataContext ctx, string name, byte[] value)
        {
            var key = ctx.Keys.FirstOrDefault(p => p.Name == name);
            if (key == null)
            {
                key = new Key { Name = name, Value = value };
                ctx.Keys.Add(key);
            }
            else
            {
                key.Value = value;
            }
        }

        private Dictionary<UInt160, UserWalletAccount> LoadAccounts()
        {
            using (var ctx = new WalletDataContext(this.path))
            {
                var accounts = ctx.Addresses
                    .Select(p => p.ScriptHash)
                    .AsEnumerable()
                    .Select(p => new UserWalletAccount(new UInt160(p)))
                    .ToDictionary(p => p.ScriptHash);

                var contracts = ctx.Contracts.Include(p => p.Account);
                foreach (var contract in contracts)
                {
                    var verificationContract = contract.RawData.AsSerializable<VerificationContract>();
                    var account = accounts[verificationContract.ScriptHash];
                    account.Contract = verificationContract;

                    var decryptedPrivateKey = this.DecryptPrivateKey(contract.Account.PrivateKeyEncrypted);
                    account.Key = new KeyPair(decryptedPrivateKey);
                }

                return accounts;
            }
        }

        private byte[] EncryptPrivateKey(byte[] decryptedPrivateKey) =>
            decryptedPrivateKey.AesEncrypt(this.masterKey, this.iv);

        private byte[] DecryptPrivateKey(byte[] encryptedPrivateKey)
        {
            if (encryptedPrivateKey == null)
            {
                throw new ArgumentNullException(nameof(encryptedPrivateKey));
            }

            if (encryptedPrivateKey.Length != 96)
            {
                throw new ArgumentException();
            }

            return encryptedPrivateKey.AesDecrypt(this.masterKey, this.iv);
        }

        private byte[] LoadStoredData(string name)
        {
            using (var ctx = new WalletDataContext(this.path))
            {
                return ctx.Keys.FirstOrDefault(p => p.Name == name)?.Value;
            }
        }

        private void BuildDatabase()
        {
            using (var ctx = new WalletDataContext(this.path))
            {
                ctx.Database.EnsureDeleted();
                ctx.Database.EnsureCreated();
            }
        }

        private void SaveStoredData(string name, byte[] value)
        {
            lock (this.lockObject)
            {
                using (var ctx = new WalletDataContext(this.path))
                {
                    UserWallet.SaveStoredData(ctx, name, value);
                    ctx.SaveChanges();
                }
            }
        }

        private void AddAccount(UserWalletAccount account, bool isImport)
        {
            lock (this.accounts)
            {
                if (this.accounts.TryGetValue(account.ScriptHash, out UserWalletAccount oldAccount))
                {
                    if (account.Contract == null)
                    {
                        account.Contract = oldAccount.Contract;
                    }
                }
                else
                {
                    var height = isImport ? 0 : Blockchain.Instance.Height;
                    this.indexer.RegisterAccounts(new[] { account.ScriptHash }, height);
                }

                this.accounts[account.ScriptHash] = account;
            }

            lock (this.lockObject)
            {
                using (var ctx = new WalletDataContext(this.path))
                {
                    if (account.HasKey)
                    {
                        var decryptedPrivateKey = new byte[96];
                        Buffer.BlockCopy(account.Key.PublicKey.EncodePoint(false), 1, decryptedPrivateKey, 0, 64);
                        Buffer.BlockCopy(account.Key.PrivateKey, 0, decryptedPrivateKey, 64, 32);

                        var encryptedPrivateKey = this.EncryptPrivateKey(decryptedPrivateKey);
                        Array.Clear(decryptedPrivateKey, 0, decryptedPrivateKey.Length);

                        var accountFromDb = ctx.Accounts
                            .FirstOrDefault(p => p.PublicKeyHash.SequenceEqual(account.Key.PublicKeyHash.ToArray()));

                        if (accountFromDb == null)
                        {
                            var accountToAdd = new Account
                            {
                                PrivateKeyEncrypted = encryptedPrivateKey,
                                PublicKeyHash = account.Key.PublicKeyHash.ToArray()
                            };

                            accountFromDb = ctx.Accounts.Add(accountToAdd).Entity;
                        }
                        else
                        {
                            accountFromDb.PrivateKeyEncrypted = encryptedPrivateKey;
                        }
                    }

                    if (account.Contract != null)
                    {
                        var accountHash = account.Contract.ScriptHash.ToArray();
                        var contractFromDb = ctx.Contracts.FirstOrDefault(p => p.ScriptHash.SequenceEqual(accountHash));
                        if (contractFromDb != null)
                        {
                            contractFromDb.PublicKeyHash = account.Key.PublicKeyHash.ToArray();
                        }
                        else
                        {
                            var contractToAdd = new Contract
                            {
                                RawData = ((VerificationContract)account.Contract).ToArray(),
                                ScriptHash = account.Contract.ScriptHash.ToArray(),
                                PublicKeyHash = account.Key.PublicKeyHash.ToArray()
                            };

                            ctx.Contracts.Add(contractToAdd);
                        }
                    }

                    // add address
                    {
                        var addressHash = account.Contract.ScriptHash.ToArray();
                        var addressFromDb = ctx.Addresses.FirstOrDefault(p => p.ScriptHash.SequenceEqual(addressHash));

                        if (addressFromDb == null)
                        {
                            var newAddress = new Address { ScriptHash = addressHash };
                            ctx.Addresses.Add(newAddress);
                        }
                    }

                    ctx.SaveChanges();
                }
            }
        }

        private void WalletIndexerWalletTransaction(object sender, WalletTransactionEventArgs e)
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
