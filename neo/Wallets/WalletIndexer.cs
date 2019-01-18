using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Data.LevelDB;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Wallets
{
    public class WalletIndexer : IDisposable
    {
        private readonly Dictionary<uint, HashSet<UInt160>> indexes = new Dictionary<uint, HashSet<UInt160>>();
        private readonly Dictionary<UInt160, HashSet<CoinReference>> trackedAccounts = new Dictionary<UInt160, HashSet<CoinReference>>();
        private readonly Dictionary<CoinReference, Coin> trackedCoins = new Dictionary<CoinReference, Coin>();
        private readonly DB db;
        private readonly Thread thread;
        private readonly object syncRoot = new object();
        private bool disposed = false;

        public WalletIndexer(string path)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);

            this.db = DB.Open(path, new Options { CreateIfMissing = true });
            if (this.db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.SYSVersion), out Slice value) 
                && Version.TryParse(value.ToString(), out Version version) 
                && version >= Version.Parse("2.5.4"))
            {
                var readOptions = new ReadOptions { FillCache = false };
                var prefix = SliceBuilder.Begin(DataEntryPrefix.IXGroup);
                var groups = this.db.Find(
                    readOptions,
                    prefix,
                    (k, v) => new
                    {
                        Height = k.ToUInt32(1),
                        Id = v.ToArray()
                    });

                foreach (var group in groups)
                {
                    var key = SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(group.Id);
                    var slice = this.db.Get(readOptions, key);
                    var accounts = slice.ToArray().AsSerializableArray<UInt160>();

                    this.indexes.Add(group.Height, new HashSet<UInt160>(accounts));

                    foreach (var accountHash in accounts)
                    {
                        this.trackedAccounts.Add(accountHash, new HashSet<CoinReference>());
                    }
                }

                var coinsPrefix = SliceBuilder.Begin(DataEntryPrefix.STCoin);
                var coins = this.db.Find(
                    options: readOptions,
                    prefix: coinsPrefix,
                    resultSelector: (k, v) => new Coin
                    {
                        Reference = k.ToArray().Skip(1).ToArray().AsSerializable<CoinReference>(),
                        Output = v.ToArray().AsSerializable<TransactionOutput>(),
                        State = (CoinStates)v.ToArray()[60]
                    });

                foreach (var coin in coins)
                {
                    this.trackedAccounts[coin.Output.ScriptHash].Add(coin.Reference);
                    this.trackedCoins.Add(coin.Reference, coin);
                }
            }
            else
            {
                var batch = new WriteBatch();
                var options = new ReadOptions { FillCache = false };
                using (var it = this.db.NewIterator(options))
                {
                    for (it.SeekToFirst(); it.Valid(); it.Next())
                    {
                        batch.Delete(it.Key());
                    }
                }

                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                var key = SliceBuilder.Begin(DataEntryPrefix.SYSVersion);
                batch.Put(key, assemblyVersion);

                this.db.Write(WriteOptions.Default, batch);
            }

            this.thread = new Thread(this.ProcessBlocks)
            {
                IsBackground = true,
                Name = $"{nameof(WalletIndexer)}.{nameof(this.ProcessBlocks)}"
            };

            this.thread.Start();
        }

        public event EventHandler<WalletTransactionEventArgs> WalletTransaction;

        public uint IndexHeight
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.indexes.Count == 0)
                    {
                        return 0;
                    }

                    return this.indexes.Keys.Min();
                }
            }
        }

        public void Dispose()
        {
            this.disposed = true;
            this.thread.Join();
            this.db.Dispose();
        }

        public IEnumerable<Coin> GetCoins(IEnumerable<UInt160> accounts)
        {
            lock (this.syncRoot)
            {
                foreach (var account in accounts)
                {
                    foreach (var reference in this.trackedAccounts[account])
                    {
                        yield return this.trackedCoins[reference];
                    }
                }
            }
        }

        public IEnumerable<UInt256> GetTransactions(IEnumerable<UInt160> accounts)
        {
            var options = new ReadOptions { FillCache = false };
            var transactions = Enumerable.Empty<UInt256>();
            foreach (var account in accounts)
            {
                var prefix = SliceBuilder.Begin(DataEntryPrefix.STTransaction).Add(account);
                var accountTransactions = this.db.Find(
                    options: options, 
                    prefix: prefix, 
                    resultSelector: (k, v) => new UInt256(k.ToArray().Skip(21).ToArray()));

                transactions = transactions.Union(accountTransactions);
            }

            foreach (var hash in transactions)
            {
                yield return hash;
            }
        }

        public void RebuildIndex()
        {
            lock (this.syncRoot)
            {
                var batch = new WriteBatch();
                var readOptions = new ReadOptions { FillCache = false };
                foreach (uint height in this.indexes.Keys)
                {
                    var ixGroupKey = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                    var groupId = this.db.Get(readOptions, ixGroupKey).ToArray();

                    batch.Delete(SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height));
                    batch.Delete(SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId));
                }

                this.indexes.Clear();
                if (this.trackedAccounts.Count > 0)
                {
                    this.indexes[0] = new HashSet<UInt160>(this.trackedAccounts.Keys);
                    var groupId = WalletIndexer.GetGroupId();

                    var ixGroupKey = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(0u);
                    batch.Put(ixGroupKey, groupId);

                    var ixAccountKey = SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId);
                    var ixAccountValue = this.trackedAccounts.Keys.ToArray().ToByteArray();
                    batch.Put(ixAccountKey, ixAccountValue);

                    foreach (var coins in this.trackedAccounts.Values)
                    {
                        coins.Clear();
                    }
                }

                foreach (var reference in this.trackedCoins.Keys)
                {
                    batch.Delete(DataEntryPrefix.STCoin, reference);
                }

                this.trackedCoins.Clear();

                var prefix = SliceBuilder.Begin(DataEntryPrefix.STTransaction);
                var keys = this.db.Find(readOptions, prefix, (k, v) => k);
                foreach (var key in keys)
                {
                    batch.Delete(key);
                }

                this.db.Write(WriteOptions.Default, batch);
            }
        }

        public void RegisterAccounts(IEnumerable<UInt160> accounts, uint height = 0)
        {
            lock (this.syncRoot)
            {
                var indexExists = this.indexes.TryGetValue(height, out HashSet<UInt160> index);
                if (!indexExists)
                {
                    index = new HashSet<UInt160>();
                }

                foreach (var account in accounts)
                {
                    if (!this.trackedAccounts.ContainsKey(account))
                    {
                        index.Add(account);
                        this.trackedAccounts.Add(account, new HashSet<CoinReference>());
                    }
                }

                if (index.Count > 0)
                {
                    var batch = new WriteBatch();
                    byte[] groupId;
                    if (!indexExists)
                    {
                        this.indexes.Add(height, index);

                        groupId = WalletIndexer.GetGroupId();
                        var key = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);

                        batch.Put(key, groupId);
                    }
                    else
                    {
                        var key = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                        groupId = this.db.Get(ReadOptions.Default, key).ToArray();
                    }

                    var batchKey = SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId);
                    var batchValue = index.ToArray().ToByteArray();

                    batch.Put(batchKey, batchValue);
                    this.db.Write(WriteOptions.Default, batch);
                }
            }
        }

        public void UnregisterAccounts(IEnumerable<UInt160> accounts)
        {
            lock (this.syncRoot)
            {
                var batch = new WriteBatch();
                var options = new ReadOptions { FillCache = false };
                foreach (var account in accounts)
                {
                    if (this.trackedAccounts.TryGetValue(account, out HashSet<CoinReference> references))
                    {
                        foreach (uint height in this.indexes.Keys.ToArray())
                        {
                            var index = this.indexes[height];
                            if (index.Remove(account))
                            {
                                var groupKey = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                                var groupId = this.db.Get(options, groupKey).ToArray();
                                if (index.Count == 0)
                                {
                                    this.indexes.Remove(height);

                                    batch.Delete(SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height));
                                    batch.Delete(SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId));
                                }
                                else
                                {
                                    var key = SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId);
                                    batch.Put(key, index.ToArray().ToByteArray());
                                }

                                break;
                            }
                        }

                        this.trackedAccounts.Remove(account);
                        foreach (var reference in references)
                        {
                            batch.Delete(DataEntryPrefix.STCoin, reference);
                            this.trackedCoins.Remove(reference);
                        }

                        var accountPrefix = SliceBuilder.Begin(DataEntryPrefix.STTransaction).Add(account);
                        var keys = this.db.Find(options, accountPrefix, (k, v) => k);
                        foreach (var key in keys)
                        {
                            batch.Delete(key);
                        }
                    }
                }

                this.db.Write(WriteOptions.Default, batch);
            }
        }

        private static byte[] GetGroupId()
        {
            var groupId = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(groupId);
            }

            return groupId;
        }

        private (Transaction, UInt160[])[] ProcessBlock(Block block, HashSet<UInt160> accounts, WriteBatch batch)
        {
            var changes = new List<(Transaction, UInt160[])>();
            foreach (var tx in block.Transactions)
            {
                var changedAccounts = new HashSet<UInt160>();
                for (ushort index = 0; index < tx.Outputs.Length; index++)
                {
                    var output = tx.Outputs[index];
                    if (this.trackedAccounts.ContainsKey(output.ScriptHash))
                    {
                        var reference = new CoinReference
                        {
                            PrevHash = tx.Hash,
                            PrevIndex = index
                        };

                        if (this.trackedCoins.TryGetValue(reference, out Coin coin))
                        {
                            coin.State |= CoinStates.Confirmed;
                        }
                        else
                        {
                            coin = new Coin
                            {
                                Reference = reference,
                                Output = output,
                                State = CoinStates.Confirmed
                            };

                            this.trackedCoins.Add(reference, coin);
                            this.trackedAccounts[output.ScriptHash].Add(reference);
                        }

                        var key = SliceBuilder.Begin(DataEntryPrefix.STCoin).Add(reference);
                        var value = SliceBuilder.Begin().Add(output).Add((byte)coin.State);
                        batch.Put(key, value);

                        changedAccounts.Add(output.ScriptHash);
                    }
                }

                foreach (var input in tx.Inputs)
                {
                    if (this.trackedCoins.TryGetValue(input, out Coin coin))
                    {
                        if (coin.Output.AssetId.Equals(Blockchain.GoverningToken.Hash))
                        {
                            coin.State |= CoinStates.Spent | CoinStates.Confirmed;

                            var key = SliceBuilder.Begin(DataEntryPrefix.STCoin).Add(input);
                            var value = SliceBuilder.Begin().Add(coin.Output).Add((byte)coin.State);
                            batch.Put(key, value);
                        }
                        else
                        {
                            this.trackedAccounts[coin.Output.ScriptHash].Remove(input);
                            this.trackedCoins.Remove(input);
                            batch.Delete(DataEntryPrefix.STCoin, input);
                        }

                        changedAccounts.Add(coin.Output.ScriptHash);
                    }
                }

                switch (tx)
                {
                    case MinerTransaction _:
                    case ContractTransaction _:
#pragma warning disable CS0612
                    case PublishTransaction _:
#pragma warning restore CS0612
                        break;
                    case ClaimTransaction claimTransaction:
                        foreach (CoinReference claim in claimTransaction.Claims)
                        {
                            if (this.trackedCoins.TryGetValue(claim, out Coin coin))
                            {
                                this.trackedAccounts[coin.Output.ScriptHash].Remove(claim);
                                this.trackedCoins.Remove(claim);
                                batch.Delete(DataEntryPrefix.STCoin, claim);

                                changedAccounts.Add(coin.Output.ScriptHash);
                            }
                        }

                        break;
#pragma warning disable CS0612
                    case EnrollmentTransaction enrollmentTransaction:
                        if (this.trackedAccounts.ContainsKey(enrollmentTransaction.ScriptHash))
                        {
                            changedAccounts.Add(enrollmentTransaction.ScriptHash);
                        }

                        break;
                    case RegisterTransaction registerTransaction:
                        if (this.trackedAccounts.ContainsKey(registerTransaction.OwnerScriptHash))
                        {
                            changedAccounts.Add(registerTransaction.OwnerScriptHash);
                        }

                        break;
#pragma warning restore CS0612
                    default:
                        foreach (var hash in tx.Witnesses.Select(p => p.ScriptHash))
                        {
                            if (this.trackedAccounts.ContainsKey(hash))
                            {
                                changedAccounts.Add(hash);
                            }
                        }

                        break;
                }

                if (changedAccounts.Any())
                {
                    foreach (var account in changedAccounts)
                    {
                        var key = SliceBuilder.Begin(DataEntryPrefix.STTransaction).Add(account).Add(tx.Hash);
                        batch.Put(key, false);
                    }
                    
                    changes.Add((tx, changedAccounts.ToArray()));
                }
            }

            return changes.ToArray();
        }

        private void ProcessBlocks()
        {
            while (!this.disposed)
            {
                while (!this.disposed)
                {
                    Block block;
                    (Transaction, UInt160[])[] changes;
                    lock (this.syncRoot)
                    {
                        if (this.indexes.Count == 0)
                        {
                            break;
                        }

                        var height = this.indexes.Keys.Min();
                        block = Blockchain.Instance.Store.GetBlock(height);
                        if (block == null)
                        {
                            break;
                        }

                        var batch = new WriteBatch();
                        var accounts = this.indexes[height];

                        changes = this.ProcessBlock(block, accounts, batch);

                        var readOptions = ReadOptions.Default;
                        var sliceBuilder = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                        var groupId = this.db.Get(readOptions, sliceBuilder).ToArray();

                        this.indexes.Remove(height);
                        batch.Delete(SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height));

                        height++;

                        if (this.indexes.TryGetValue(height, out HashSet<UInt160> nextAccounts))
                        {
                            nextAccounts.UnionWith(accounts);

                            var groupIdKey = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                            groupId = this.db.Get(readOptions, groupIdKey).ToArray();

                            var nextAccountsKey = SliceBuilder.Begin(DataEntryPrefix.IXAccounts).Add(groupId);
                            batch.Put(nextAccountsKey, nextAccounts.ToArray().ToByteArray());
                        }
                        else
                        {
                            this.indexes.Add(height, accounts);

                            var ixGroupKey = SliceBuilder.Begin(DataEntryPrefix.IXGroup).Add(height);
                            batch.Put(ixGroupKey, groupId);
                        }

                        this.db.Write(WriteOptions.Default, batch);
                    }

                    foreach (var (tx, accounts) in changes)
                    {
                        var transactionEventArgs = new WalletTransactionEventArgs
                        {
                            Transaction = tx,
                            RelatedAccounts = accounts,
                            Height = block.Index,
                            Time = block.Timestamp
                        };

                        this.WalletTransaction?.Invoke(null, transactionEventArgs);
                    }
                }

                for (int i = 0; i < 20 && !this.disposed; i++)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
