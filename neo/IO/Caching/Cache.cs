using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Neo.IO.Caching
{
    internal abstract class Cache<TKey, TValue> : ICollection<TValue>, IDisposable
    {
        private readonly int maxCapacity;

        protected readonly ReaderWriterLockSlim RwSyncRootLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        protected readonly Dictionary<TKey, CacheItem> InnerDictionary = new Dictionary<TKey, CacheItem>();

        public Cache(int maxCapacity)
        {
            this.maxCapacity = maxCapacity;
        }

        public int Count
        {
            get
            {
                this.RwSyncRootLock.EnterReadLock();
                try
                {
                    return this.InnerDictionary.Count;
                }
                finally
                {
                    this.RwSyncRootLock.ExitReadLock();
                }
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                this.RwSyncRootLock.EnterReadLock();
                try
                {
                    if (!this.InnerDictionary.TryGetValue(key, out CacheItem item))
                    {
                        throw new KeyNotFoundException();
                    }

                    this.OnAccess(item);
                    return item.Value;
                }
                finally
                {
                    this.RwSyncRootLock.ExitReadLock();
                }
            }
        }

        public void Add(TValue item)
        {
            TKey key = this.GetKeyForItem(item);
            this.RwSyncRootLock.EnterWriteLock();
            try
            {
                this.AddInternal(key, item);
            }
            finally
            {
                this.RwSyncRootLock.ExitWriteLock();
            }
        }

        private void AddInternal(TKey key, TValue item)
        {
            if (this.InnerDictionary.TryGetValue(key, out CacheItem cacheItem))
            {
                this.OnAccess(cacheItem);
            }
            else
            {
                if (this.InnerDictionary.Count >= this.maxCapacity)
                {
                    // TODO: 对PLINQ查询进行性能测试，以便确定此处使用何种算法更优（并行或串行）
                    var itemsToRemove = this.InnerDictionary
                        .Values
                        .AsParallel()
                        .OrderBy(p => p.Time)
                        .Take(this.InnerDictionary.Count - this.maxCapacity + 1);

                    foreach (var itemToRemove in itemsToRemove)
                    {
                        this.RemoveInternal(itemToRemove);
                    }
                }

                this.InnerDictionary.Add(key, new CacheItem(key, item));
            }
        }

        public void AddRange(IEnumerable<TValue> items)
        {
            this.RwSyncRootLock.EnterWriteLock();
            try
            {
                foreach (TValue item in items)
                {
                    TKey key = this.GetKeyForItem(item);
                    this.AddInternal(key, item);
                }
            }
            finally
            {
                this.RwSyncRootLock.ExitWriteLock();
            }
        }

        public void Clear()
        {
            this.RwSyncRootLock.EnterWriteLock();
            try
            {
                var itemsToRemove = this.InnerDictionary.Values.ToArray();
                foreach (var item in itemsToRemove)
                {
                    this.RemoveInternal(item);
                }
            }
            finally
            {
                this.RwSyncRootLock.ExitWriteLock();
            }
        }

        public bool Contains(TKey key)
        {
            this.RwSyncRootLock.EnterReadLock();
            try
            {
                if (!this.InnerDictionary.TryGetValue(key, out CacheItem cacheItem))
                {
                    return false;
                }

                this.OnAccess(cacheItem);
                return true;
            }
            finally
            {
                this.RwSyncRootLock.ExitReadLock();
            }
        }

        public bool Contains(TValue item) => this.Contains(this.GetKeyForItem(item));
        
        public void CopyTo(TValue[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException();
            }

            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (arrayIndex + this.InnerDictionary.Count > array.Length)
            {
                throw new ArgumentException();
            }

            foreach (var item in this)
            {
                array[arrayIndex++] = item;
            }
        }

        public void Dispose()
        {
            this.Clear();
            this.RwSyncRootLock.Dispose();
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            this.RwSyncRootLock.EnterReadLock();
            try
            {
                foreach (TValue item in this.InnerDictionary.Values.Select(p => p.Value))
                {
                    yield return item;
                }
            }
            finally
            {
                this.RwSyncRootLock.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        
        protected abstract TKey GetKeyForItem(TValue item);

        public bool Remove(TKey key)
        {
            this.RwSyncRootLock.EnterWriteLock();
            try
            {
                if (!this.InnerDictionary.TryGetValue(key, out CacheItem cacheItem))
                {
                    return false;
                }

                this.RemoveInternal(cacheItem);
                return true;
            }
            finally
            {
                this.RwSyncRootLock.ExitWriteLock();
            }
        }

        public bool Remove(TValue item) => this.Remove(this.GetKeyForItem(item));
        
        public bool TryGet(TKey key, out TValue item)
        {
            this.RwSyncRootLock.EnterReadLock();
            try
            {
                if (this.InnerDictionary.TryGetValue(key, out CacheItem cacheItem))
                {
                    this.OnAccess(cacheItem);
                    item = cacheItem.Value;
                    return true;
                }
            }
            finally
            {
                this.RwSyncRootLock.ExitReadLock();
            }

            item = default(TValue);
            return false;
        }

        protected abstract void OnAccess(CacheItem item);

        private void RemoveInternal(CacheItem item)
        {
            this.InnerDictionary.Remove(item.Key);
            if (item.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        protected class CacheItem
        {
            public TKey Key;
            public TValue Value;
            public DateTime Time;

            public CacheItem(TKey key, TValue value)
            {
                this.Key = key;
                this.Value = value;
                this.Time = DateTime.Now;
            }
        }
    }
}
