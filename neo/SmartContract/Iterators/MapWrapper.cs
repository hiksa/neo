using System.Collections.Generic;
using Neo.VM;

namespace Neo.SmartContract.Iterators
{
    internal class MapWrapper : IIterator
    {
        private readonly IEnumerator<KeyValuePair<StackItem, StackItem>> enumerator;

        public MapWrapper(IEnumerable<KeyValuePair<StackItem, StackItem>> map) =>
            this.enumerator = map.GetEnumerator();

        public void Dispose() => this.enumerator.Dispose();

        public StackItem Key() => this.enumerator.Current.Key;

        public bool Next() => this.enumerator.MoveNext();

        public StackItem Value() => this.enumerator.Current.Value;
    }
}
