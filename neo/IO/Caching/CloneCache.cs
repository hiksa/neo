using System;
using System.Collections.Generic;

namespace Neo.IO.Caching
{
    internal class CloneCache<TKey, TValue> : DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        private DataCache<TKey, TValue> innerCache;

        public CloneCache(DataCache<TKey, TValue> innerCache) => 
            this.innerCache = innerCache;        

        protected override void AddInternal(TKey key, TValue value) => 
            this.innerCache.Add(key, value);

        public override void DeleteInternal(TKey key) => 
            this.innerCache.Delete(key);        

        protected override IEnumerable<KeyValuePair<TKey, TValue>> FindInternal(byte[] keyPrefix)
        {
            foreach (KeyValuePair<TKey, TValue> pair in this.innerCache.Find(keyPrefix))
            {
                yield return new KeyValuePair<TKey, TValue>(pair.Key, pair.Value.Clone());
            }
        }

        protected override TValue GetInternal(TKey key) =>
            this.innerCache[key].Clone();        

        protected override TValue TryGetInternal(TKey key) =>
            this.innerCache.TryGet(key)?.Clone();

        protected override void UpdateInternal(TKey key, TValue value) =>
            this.innerCache.GetAndChange(key).FromReplica(value);        
    }
}
