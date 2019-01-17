using System;

namespace Neo.IO.Caching
{
    public abstract class MetaDataCache<T>
        where T : class, ICloneable<T>, ISerializable, new()
    {
        private T Item;
        private TrackState State;
        private readonly Func<T> factory;

        protected MetaDataCache(Func<T> factory)
        {
            this.factory = factory;
        }

        protected abstract void AddInternal(T item);

        protected abstract T TryGetInternal();

        protected abstract void UpdateInternal(T item);

        public void Commit()
        {
            switch (this.State)
            {
                case TrackState.Added:
                    this.AddInternal(this.Item);
                    break;
                case TrackState.Changed:
                    this.UpdateInternal(this.Item);
                    break;
            }
        }

        public MetaDataCache<T> CreateSnapshot() => new CloneMetaCache<T>(this);
        
        public T Get()
        {
            if (this.Item == null)
            {
                this.Item = this.TryGetInternal();
            }

            if (this.Item == null)
            {
                this.Item = this.factory?.Invoke() ?? new T();
                this.State = TrackState.Added;
            }

            return this.Item;
        }

        public T GetAndChange()
        {
            var item = this.Get();
            if (this.State == TrackState.None)
            {
                this.State = TrackState.Changed;
            }

            return item;
        }
    }
}
