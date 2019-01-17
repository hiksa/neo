using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.IO.Caching
{
    public abstract class DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        private readonly Dictionary<TKey, Trackable> dictionary = new Dictionary<TKey, Trackable>();

        public TValue this[TKey key]
        {
            get
            {
                lock (this.dictionary)
                {
                    if (this.dictionary.TryGetValue(key, out Trackable trackable))
                    {
                        if (trackable.State == TrackState.Deleted)
                        {
                            throw new KeyNotFoundException();
                        }
                    }
                    else
                    {
                        trackable = new Trackable
                        {
                            Key = key,
                            Item = this.GetInternal(key),
                            State = TrackState.None
                        };

                        this.dictionary.Add(key, trackable);
                    }

                    return trackable.Item;
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            lock (this.dictionary)
            {
                if (this.dictionary.TryGetValue(key, out Trackable trackable) && trackable.State != TrackState.Deleted)
                {
                    throw new ArgumentException();
                }

                this.dictionary[key] = new Trackable
                {
                    Key = key,
                    Item = value,
                    State = trackable == null ? TrackState.Added : TrackState.Changed
                };
            }
        }

        protected abstract void AddInternal(TKey key, TValue value);

        public void Commit()
        {
            foreach (var trackable in this.GetChangeSet())
            {
                switch (trackable.State)
                {
                    case TrackState.Added:
                        this.AddInternal(trackable.Key, trackable.Item);
                        break;
                    case TrackState.Changed:
                        this.UpdateInternal(trackable.Key, trackable.Item);
                        break;
                    case TrackState.Deleted:
                        this.DeleteInternal(trackable.Key);
                        break;
                }
            }
        }

        public DataCache<TKey, TValue> CreateSnapshot() =>
            new CloneCache<TKey, TValue>(this);

        public void Delete(TKey key)
        {
            lock (this.dictionary)
            {
                if (this.dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Added)
                    {
                        this.dictionary.Remove(key);
                    }
                    else
                    {
                        trackable.State = TrackState.Deleted;
                    }
                }
                else
                {
                    var item = this.TryGetInternal(key);
                    if (item == null)
                    {
                        return;
                    }

                    var newEntry = new Trackable
                    {
                        Key = key,
                        Item = item,
                        State = TrackState.Deleted
                    };

                    this.dictionary.Add(key, newEntry);
                }
            }
        }

        public abstract void DeleteInternal(TKey key);

        public void DeleteWhere(Func<TKey, TValue, bool> predicate)
        {
            lock (this.dictionary)
            {
                var itemsToDelete = this.dictionary
                    .Where(p => p.Value.State != TrackState.Deleted && predicate(p.Key, p.Value.Item))
                    .Select(p => p.Value);

                foreach (var item in itemsToDelete)
                {
                    item.State = TrackState.Deleted;
                }
            }
        }

        public IEnumerable<KeyValuePair<TKey, TValue>> Find(byte[] keyPrefix = null)
        {
            lock (this.dictionary)
            {
                foreach (var pair in this.FindInternal(keyPrefix ?? new byte[0]))
                {
                    if (!this.dictionary.ContainsKey(pair.Key))
                    {
                        yield return pair;
                    }
                }

                foreach (var pair in this.dictionary)
                {
                    if (pair.Value.State != TrackState.Deleted 
                        && (keyPrefix == null || pair.Key.ToArray().Take(keyPrefix.Length).SequenceEqual(keyPrefix)))
                    {
                        yield return new KeyValuePair<TKey, TValue>(pair.Key, pair.Value.Item);
                    }
                }
            }
        }

        protected abstract IEnumerable<KeyValuePair<TKey, TValue>> FindInternal(byte[] keyPrefix);

        public IEnumerable<Trackable> GetChangeSet()
        {
            lock (this.dictionary)
            {
                foreach (Trackable trackable in this.dictionary.Values.Where(p => p.State != TrackState.None))
                {
                    yield return trackable;
                }
            }
        }

        protected abstract TValue GetInternal(TKey key);

        public TValue GetAndChange(TKey key, Func<TValue> factory = null)
        {
            lock (this.dictionary)
            {
                if (this.dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Deleted)
                    {
                        if (factory == null)
                        {
                            throw new KeyNotFoundException();
                        }

                        trackable.Item = factory();
                        trackable.State = TrackState.Changed;
                    }
                    else if (trackable.State == TrackState.None)
                    {
                        trackable.State = TrackState.Changed;
                    }
                }
                else
                {
                    trackable = new Trackable
                    {
                        Key = key,
                        Item = this.TryGetInternal(key)
                    };

                    if (trackable.Item == null)
                    {
                        if (factory == null)
                        {
                            throw new KeyNotFoundException();
                        }

                        trackable.Item = factory();
                        trackable.State = TrackState.Added;
                    }
                    else
                    {
                        trackable.State = TrackState.Changed;
                    }

                    this.dictionary.Add(key, trackable);
                }

                return trackable.Item;
            }
        }

        public TValue GetOrAdd(TKey key, Func<TValue> factory)
        {
            lock (this.dictionary)
            {
                if (this.dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Deleted)
                    {
                        trackable.Item = factory();
                        trackable.State = TrackState.Changed;
                    }
                }
                else
                {
                    trackable = new Trackable
                    {
                        Key = key,
                        Item = this.TryGetInternal(key)
                    };

                    if (trackable.Item == null)
                    {
                        trackable.Item = factory();
                        trackable.State = TrackState.Added;
                    }
                    else
                    {
                        trackable.State = TrackState.None;
                    }

                    this.dictionary.Add(key, trackable);
                }

                return trackable.Item;
            }
        }

        public TValue TryGet(TKey key)
        {
            lock (this.dictionary)
            {
                if (this.dictionary.TryGetValue(key, out Trackable trackable))
                {
                    return trackable.State == TrackState.Deleted ? null : trackable.Item;
                }

                var value = this.TryGetInternal(key);
                if (value == null)
                {
                    return null;
                }

                var newEntry = new Trackable
                {
                    Key = key,
                    Item = value,
                    State = TrackState.None
                };

                this.dictionary.Add(key, newEntry);
                return value;
            }
        }

        protected abstract TValue TryGetInternal(TKey key);

        protected abstract void UpdateInternal(TKey key, TValue value);

        public class Trackable
        {
            public TKey Key;
            public TValue Item;
            public TrackState State;
        }
    }
}
