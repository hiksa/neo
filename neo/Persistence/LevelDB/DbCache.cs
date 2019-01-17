using System;
using System.Collections.Generic;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;

namespace Neo.Persistence.LevelDB
{
    internal class DbCache<TKey, TValue> : DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable, new()
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteBatch batch;
        private readonly byte prefix;

        public DbCache(DB db, ReadOptions options, WriteBatch batch, byte prefix)
        {
            this.db = db;
            this.options = options ?? ReadOptions.Default;
            this.batch = batch;
            this.prefix = prefix;
        }

        public override void DeleteInternal(TKey key) =>
            this.batch?.Delete(this.prefix, key);

        protected override void AddInternal(TKey key, TValue value) =>
            this.batch?.Put(this.prefix, key, value);

        protected override IEnumerable<KeyValuePair<TKey, TValue>> FindInternal(byte[] keyPrefix) =>
            this.db.Find(
                options: this.options, 
                prefix: SliceBuilder.Begin(this.prefix).Add(keyPrefix), 
                resultSelector: (k, v) => new KeyValuePair<TKey, TValue>(
                    k.ToArray().AsSerializable<TKey>(1), 
                    v.ToArray().AsSerializable<TValue>()));

        protected override TValue GetInternal(TKey key) =>
            this.db.Get<TValue>(this.options, this.prefix, key);

        protected override TValue TryGetInternal(TKey key) =>
            this.db.TryGet<TValue>(this.options, this.prefix, key);
        
        protected override void UpdateInternal(TKey key, TValue value) =>
            this.batch?.Put(this.prefix, key, value);
    }
}
