using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.Wallets
{
    public abstract class Wallet : IDisposable
    {
        private static readonly Random Random = new Random();

        public abstract event EventHandler<WalletTransactionEventArgs> WalletTransaction;

        public abstract string Name { get; }

        public abstract Version Version { get; }

        public abstract uint WalletHeight { get; }
        
        public static byte[] GetPrivateKeyFromNEP2(
            string nep2, 
            string passphrase, 
            int N = 16384, 
            int r = 8, 
            int p = 8)
        {
            if (nep2 == null)
            {
                throw new ArgumentNullException(nameof(nep2));
            }

            if (passphrase == null)
            {
                throw new ArgumentNullException(nameof(passphrase));
            }

            byte[] data = nep2.Base58CheckDecode();
            if (data.Length != 39 || data[0] != 0x01 || data[1] != 0x42 || data[2] != 0xe0)
            {
                throw new FormatException();
            }

            byte[] addresshash = new byte[4];
            Buffer.BlockCopy(data, 3, addresshash, 0, 4);
            byte[] derivedkey = SCrypt.DeriveKey(Encoding.UTF8.GetBytes(passphrase), addresshash, N, r, p, 64);
            byte[] derivedhalf1 = derivedkey.Take(32).ToArray();
            byte[] derivedhalf2 = derivedkey.Skip(32).ToArray();
            byte[] encryptedkey = new byte[32];

            Buffer.BlockCopy(data, 7, encryptedkey, 0, 32);
            byte[] privateKey = XOR(encryptedkey.AES256Decrypt(derivedhalf2), derivedhalf1);

            ECPoint pubkey = Cryptography.ECC.ECCurve.Secp256r1.G * privateKey;
            UInt160 scriptHash = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            string address = scriptHash.ToAddress();

            if (!Encoding.ASCII.GetBytes(address).Sha256().Sha256().Take(4).SequenceEqual(addresshash))
            {
                throw new FormatException();
            }

            return privateKey;
        }

        public static byte[] GetPrivateKeyFromWIF(string wif)
        {
            if (wif == null)
            {
                throw new ArgumentNullException();
            }

            byte[] data = wif.Base58CheckDecode();
            if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
            {
                throw new FormatException();
            }

            byte[] privateKey = new byte[32];
            Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
            Array.Clear(data, 0, data.Length);
            return privateKey;
        }

        public abstract void ApplyTransaction(Transaction tx);
        public abstract bool Contains(UInt160 scriptHash);
        public abstract WalletAccount CreateAccount(byte[] privateKey);
        public abstract WalletAccount CreateAccount(Contract contract, KeyPair key = null);
        public abstract WalletAccount CreateAccount(UInt160 scriptHash);
        public abstract bool DeleteAccount(UInt160 scriptHash);
        public abstract WalletAccount GetAccount(UInt160 scriptHash);
        public abstract IEnumerable<WalletAccount> GetAccounts();
        public abstract IEnumerable<Coin> GetCoins(IEnumerable<UInt160> accounts);
        public abstract IEnumerable<UInt256> GetTransactions();
        public abstract bool VerifyPassword(string password);

        public WalletAccount CreateAccount()
        {
            var privateKey = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }

            var account = this.CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public WalletAccount CreateAccount(Contract contract, byte[] privateKey)
        {
            if (privateKey == null)
            {
                return this.CreateAccount(contract);
            }

            return this.CreateAccount(contract, new KeyPair(privateKey));
        }

        public virtual void Dispose()
        {
        }

        public IEnumerable<Coin> FindUnspentCoins(params UInt160[] from)
        {
            var accountHashes = from.Length > 0
                ? from
                : this.GetAccounts()
                    .Where(p => !p.Lock && !p.WatchOnly)
                    .Select(p => p.ScriptHash);

            var coins = this.GetCoins(accountHashes)
                .Where(p => p.State.HasFlag(CoinStates.Confirmed) && p.State.HasNoFlags(CoinStates.Spent, CoinStates.Frozen));

            return coins;
        }

        public virtual Coin[] FindUnspentCoins(UInt256 assetId, Fixed8 amount, params UInt160[] from) =>
            FindUnspentCoins(this.FindUnspentCoins(from), assetId, amount);

        public WalletAccount GetAccount(ECPoint pubkey) =>
            this.GetAccount(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash());

        public Fixed8 GetAvailable(UInt256 assetId) =>
            this.FindUnspentCoins().Where(p => p.Output.AssetId.Equals(assetId)).Sum(p => p.Output.Value);

        public BigDecimal GetAvailable(UIntBase assetId)
        {
            if (assetId is UInt160 assetIdUnboxed)
            {
                byte[] script;
                var accounts = this.GetAccounts().Where(p => !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitPush(0);
                    foreach (UInt160 account in accounts)
                    {
                        sb.EmitAppCall(assetIdUnboxed, "balanceOf", account);
                        sb.Emit(OpCode.ADD);
                    }

                    sb.EmitAppCall(assetIdUnboxed, "decimals");
                    script = sb.ToArray();
                }

                var engine = ApplicationEngine.Run(script, extraGAS: Fixed8.FromDecimal(0.2m) * accounts.Length);
                if (engine.State.HasFlag(VMState.FAULT))
                {
                    return new BigDecimal(0, 0);
                }

                var decimals = (byte)engine.ResultStack.Pop().GetBigInteger();
                var amount = engine.ResultStack.Pop().GetBigInteger();

                return new BigDecimal(amount, decimals);
            }
            else
            {
                return new BigDecimal(this.GetAvailable((UInt256)assetId).GetData(), 8);
            }
        }

        public Fixed8 GetBalance(UInt256 assetId) =>
            this.GetCoins(this.GetAccounts().Select(p => p.ScriptHash))
                .Where(p => !p.State.HasFlag(CoinStates.Spent) && p.Output.AssetId.Equals(assetId))
                .Sum(p => p.Output.Value);

        public virtual UInt160 GetChangeAddress()
        {
            var accounts = this.GetAccounts().ToArray();
            var account = accounts.FirstOrDefault(p => p.IsDefault);
            if (account == null)
            {
                account = accounts.FirstOrDefault(p => p.Contract?.Script.IsSignatureContract() == true);
            }

            if (account == null)
            {
                account = accounts.FirstOrDefault(p => !p.WatchOnly);
            }

            if (account == null)
            {
                account = accounts.FirstOrDefault();
            }

            return account?.ScriptHash;
        }

        public IEnumerable<Coin> GetCoins() =>
            this.GetCoins(this.GetAccounts().Select(p => p.ScriptHash));

        public IEnumerable<Coin> GetUnclaimedCoins()
        {
            var accountHashes = this.GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash);
            var coins = this.GetCoins(accountHashes)
                .Where(p => p.Output.AssetId.Equals(Blockchain.GoverningToken.Hash))
                .Where(p => p.State.HasAllFlags(CoinStates.Confirmed, CoinStates.Spent))
                .Where(p => p.State.HasNoFlags(CoinStates.Claimed, CoinStates.Frozen));

            return coins;
        }

        public virtual WalletAccount Import(X509Certificate2 cert)
        {
            byte[] privateKey;
            using (ECDsa ecdsa = cert.GetECDsaPrivateKey())
            {
                privateKey = ecdsa.ExportParameters(true).D;
            }

            var account = this.CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public virtual WalletAccount Import(string wif)
        {
            byte[] privateKey = GetPrivateKeyFromWIF(wif);
            WalletAccount account = this.CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public virtual WalletAccount Import(string nep2, string passphrase)
        {
            var privateKey = GetPrivateKeyFromNEP2(nep2, passphrase);
            var account = this.CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public T MakeTransaction<T>(T tx, UInt160 from = null, UInt160 change_address = null, Fixed8 fee = default(Fixed8)) where T : Transaction
        {
            if (tx.Outputs == null)
            {
                tx.Outputs = new TransactionOutput[0];
            }

            if (tx.Attributes == null)
            {
                tx.Attributes = new TransactionAttribute[0];
            }

            fee += tx.SystemFee;

            var outputs = typeof(T) == typeof(IssueTransaction)
                ? new TransactionOutput[0]
                : tx.Outputs;

            var pay_total = outputs
                .GroupBy(
                    p => p.AssetId, 
                    (k, g) => new { AssetId = k, Value = g.Sum(p => p.Value) })
                .ToDictionary(p => p.AssetId);

            if (fee > Fixed8.Zero)
            {
                if (pay_total.ContainsKey(Blockchain.UtilityToken.Hash))
                {
                    var value = new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = pay_total[Blockchain.UtilityToken.Hash].Value + fee
                    };

                    pay_total[Blockchain.UtilityToken.Hash] = value;
                }
                else
                {
                    var value = new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = fee
                    };

                    pay_total.Add(Blockchain.UtilityToken.Hash, value);
                }
            }

            var pay_coins = pay_total
                .Select(p => new
                {
                    AssetId = p.Key,
                    Unspents = from == null 
                        ? this.FindUnspentCoins(p.Key, p.Value.Value) 
                        : this.FindUnspentCoins(p.Key, p.Value.Value, from)
                })
                .ToDictionary(p => p.AssetId);

            if (pay_coins.Any(p => p.Value.Unspents == null))
            {
                return null;
            }

            var sumOfInputs = pay_coins.Values.ToDictionary(
                p => p.AssetId,
                p => new
                {
                    p.AssetId,
                    Value = p.Unspents.Sum(q => q.Output.Value)
                });

            if (change_address == null)
            {
                change_address = this.GetChangeAddress();
            }

            List<TransactionOutput> outputs_new = new List<TransactionOutput>(tx.Outputs);
            foreach (UInt256 asset_id in sumOfInputs.Keys)
            {
                if (sumOfInputs[asset_id].Value > pay_total[asset_id].Value)
                {
                    outputs_new.Add(new TransactionOutput
                    {
                        AssetId = asset_id,
                        Value = sumOfInputs[asset_id].Value - pay_total[asset_id].Value,
                        ScriptHash = change_address
                    });
                }
            }

            tx.Inputs = pay_coins.Values.SelectMany(p => p.Unspents).Select(p => p.Reference).ToArray();
            tx.Outputs = outputs_new.ToArray();
            return tx;
        }

        public Transaction MakeTransaction(
            List<TransactionAttribute> attributes, 
            IEnumerable<TransferOutput> outputs,
            UInt160 from = null, 
            UInt160 changeAddress = null,
            Fixed8 fee = default(Fixed8))
        {
            var cOutputs = outputs
                .Where(p => !p.IsGlobalAsset)
                .GroupBy(
                    p => new
                    {
                        AssetId = (UInt160)p.AssetId,
                        Account = p.ScriptHash
                    }, 
                    (k, g) => new
                    {
                        k.AssetId,
                        Value = g.Aggregate(BigInteger.Zero, (x, y) => x + y.Value.Value),
                        k.Account
                    })
                .ToArray();

            Transaction tx;
            if (attributes == null)
            {
                attributes = new List<TransactionAttribute>();
            }

            if (cOutputs.Length == 0)
            {
                tx = new ContractTransaction();
            }
            else
            {
                var accounts = from == null 
                    ? this.GetAccounts()
                        .Where(p => !p.Lock && !p.WatchOnly)
                        .Select(p => p.ScriptHash)
                        .ToArray() 
                    : new[] { from };

                var attributeHashes = new HashSet<UInt160>();
                using (var sb = new ScriptBuilder())
                {
                    foreach (var output in cOutputs)
                    {
                        var balances = new List<(UInt160 Account, BigInteger Value)>();
                        foreach (UInt160 account in accounts)
                        {
                            byte[] script;
                            using (var sb2 = new ScriptBuilder())
                            {
                                sb2.EmitAppCall(output.AssetId, "balanceOf", account);
                                script = sb2.ToArray();
                            }

                            var engine = ApplicationEngine.Run(script);
                            if (engine.State.HasFlag(VMState.FAULT))
                            {
                                return null;
                            }

                            balances.Add((account, engine.ResultStack.Pop().GetBigInteger()));
                        }

                        var sum = balances.Aggregate(BigInteger.Zero, (x, y) => x + y.Value);
                        if (sum < output.Value)
                        {
                            return null;
                        }

                        if (sum != output.Value)
                        {
                            balances = balances.OrderByDescending(p => p.Value).ToList();
                            var amount = output.Value;
                            int i = 0;
                            while (balances[i].Value <= amount)
                            {
                                amount -= balances[i++].Value;
                            }

                            if (amount == BigInteger.Zero)
                            {
                                balances = balances.Take(i).ToList();
                            }
                            else
                            {
                                balances = balances.Take(i).Concat(new[] { balances.Last(p => p.Value >= amount) }).ToList();
                            }

                            sum = balances.Aggregate(BigInteger.Zero, (x, y) => x + y.Value);
                        }

                        attributeHashes.UnionWith(balances.Select(p => p.Account));
                        for (int i = 0; i < balances.Count; i++)
                        {
                            var value = balances[i].Value;
                            if (i == 0)
                            {
                                var change = sum - output.Value;
                                if (change > 0)
                                {
                                    value -= change;
                                }
                            }

                            sb.EmitAppCall(output.AssetId, "transfer", balances[i].Account, output.Account, value);
                            sb.Emit(OpCode.THROWIFNOT);
                        }
                    }

                    byte[] nonce = new byte[8];
                    Random.NextBytes(nonce);
                    sb.Emit(OpCode.RET, nonce);
                    tx = new InvocationTransaction
                    {
                        Version = 1,
                        Script = sb.ToArray()
                    };
                }

                attributes.AddRange(attributeHashes.Select(p => new TransactionAttribute
                {
                    Usage = TransactionAttributeUsage.Script,
                    Data = p.ToArray()
                }));
            }

            tx.Attributes = attributes.ToArray();
            tx.Inputs = new CoinReference[0];
            tx.Outputs = outputs.Where(p => p.IsGlobalAsset).Select(p => p.ToTxOutput()).ToArray();
            tx.Witnesses = new Witness[0];
            if (tx is InvocationTransaction invocationTransaction)
            {
                var engine = ApplicationEngine.Run(invocationTransaction.Script, invocationTransaction);
                if (engine.State.HasFlag(VMState.FAULT))
                {
                    return null;
                }

                tx = new InvocationTransaction
                {
                    Version = invocationTransaction.Version,
                    Script = invocationTransaction.Script,
                    Gas = InvocationTransaction.GetGas(engine.GasConsumed),
                    Attributes = invocationTransaction.Attributes,
                    Inputs = invocationTransaction.Inputs,
                    Outputs = invocationTransaction.Outputs
                };
            }

            tx = this.MakeTransaction(tx, from, changeAddress, fee);
            return tx;
        }

        public bool Sign(ContractParametersContext context)
        {
            var success = false;
            foreach (var scriptHash in context.ScriptHashes)
            {
                var account = this.GetAccount(scriptHash);
                if (account?.HasKey != true)
                {
                    continue;
                }

                var key = account.GetKey();
                var signature = context.Verifiable.Sign(key);

                success |= context.AddSignature(account.Contract, key.PublicKey, signature);
            }

            return success;
        }

        protected static Coin[] FindUnspentCoins(IEnumerable<Coin> unspents, UInt256 assetId, Fixed8 amount)
        {
            var unspentCoins = unspents.Where(p => p.Output.AssetId == assetId).ToArray();
            var sum = unspentCoins.Sum(p => p.Output.Value);
            if (sum < amount)
            {
                return null;
            }

            if (sum == amount)
            {
                return unspentCoins;
            }

            var orderedUnspents = unspentCoins.OrderByDescending(p => p.Output.Value).ToArray();
            int i = 0;
            while (orderedUnspents[i].Output.Value <= amount)
            {
                amount -= orderedUnspents[i++].Output.Value;
            }

            if (amount == Fixed8.Zero)
            {
                return orderedUnspents
                    .Take(i)
                    .ToArray();
            }
            else
            {
                return orderedUnspents
                    .Take(i)
                    .Concat(new[] { orderedUnspents.Last(p => p.Output.Value >= amount) })
                    .ToArray();
            }
        }

        private static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                throw new ArgumentException();
            }

            return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
        }
    }
}
