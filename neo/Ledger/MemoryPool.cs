using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Neo.Network.P2P.Payloads;

namespace Neo.Ledger
{
    public class MemoryPool : IReadOnlyCollection<Transaction>
    {
        private readonly ConcurrentDictionary<UInt256, PoolItem> mempoolFee = new ConcurrentDictionary<UInt256, PoolItem>();
        private readonly ConcurrentDictionary<UInt256, PoolItem> mempoolFree = new ConcurrentDictionary<UInt256, PoolItem>();

        public MemoryPool(int capacity)
        {
            this.Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count => this.mempoolFee.Count + this.mempoolFree.Count;

        public void Clear()
        {
            this.mempoolFree.Clear();
            this.mempoolFee.Clear();
        }

        public bool ContainsKey(UInt256 hash) => this.mempoolFree.ContainsKey(hash) || this.mempoolFee.ContainsKey(hash);

        public IEnumerator<Transaction> GetEnumerator()
        {
            return this.mempoolFee
                .Select(p => p.Value.Transaction)
                .Concat(this.mempoolFree.Select(p => p.Value.Transaction))
                .GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public bool TryAdd(UInt256 hash, Transaction tx)
        {
            ConcurrentDictionary<UInt256, PoolItem> pool;

            if (tx.IsLowPriority)
            {
                pool = this.mempoolFree;
            }
            else
            {
                pool = this.mempoolFee;
            }

            pool.TryAdd(hash, new PoolItem(tx));

            if (this.Count > this.Capacity)
            {
                MemoryPool.RemoveOldest(this.mempoolFree, DateTime.UtcNow.AddSeconds(-Blockchain.SecondsPerBlock * 20));

                var exceed = this.Count - this.Capacity;
                if (exceed > 0)
                {
                    MemoryPool.RemoveLowestFee(this.mempoolFree, exceed);
                    exceed = this.Count - this.Capacity;

                    if (exceed > 0)
                    {
                        MemoryPool.RemoveLowestFee(this.mempoolFee, exceed);
                    }
                }
            }

            return pool.ContainsKey(hash);
        }

        public bool TryRemove(UInt256 hash, out Transaction tx)
        {
            if (this.mempoolFree.TryRemove(hash, out PoolItem item))
            {
                tx = item.Transaction;
                return true;
            }
            else if (this.mempoolFee.TryRemove(hash, out item))
            {
                tx = item.Transaction;
                return true;
            }
            else
            {
                tx = null;
                return false;
            }
        }

        public bool TryGetValue(UInt256 hash, out Transaction tx)
        {
            if (this.mempoolFree.TryGetValue(hash, out PoolItem item))
            {
                tx = item.Transaction;
                return true;
            }
            else if (this.mempoolFee.TryGetValue(hash, out item))
            {
                tx = item.Transaction;
                return true;
            }
            else
            {
                tx = null;
                return false;
            }
        }

        internal static void RemoveLowestFee(ConcurrentDictionary<UInt256, PoolItem> pool, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (count >= pool.Count)
            {
                pool.Clear();
            }
            else
            {
                var delete = pool.AsParallel()
                    .OrderBy(p => p.Value.Transaction.NetworkFee / p.Value.Transaction.Size)
                    .ThenBy(p => p.Value.Transaction.NetworkFee)
                    .ThenBy(p => new BigInteger(p.Key.ToArray()))
                    .Take(count)
                    .Select(p => p.Key)
                    .ToArray();

                foreach (var hash in delete)
                {
                    pool.TryRemove(hash, out _);
                }
            }
        }

        internal static void RemoveOldest(ConcurrentDictionary<UInt256, PoolItem> pool, DateTime time)
        {
            var hashes = pool
                .Where(p => p.Value.Timestamp < time)
                .Select(p => p.Key)
                .ToArray();

            foreach (var hash in hashes)
            {
                pool.TryRemove(hash, out _);
            }
        }
    }
}