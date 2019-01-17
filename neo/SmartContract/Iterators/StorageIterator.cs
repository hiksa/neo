using System.Collections.Generic;
using Neo.Ledger;
using Neo.VM;

namespace Neo.SmartContract.Iterators
{
    internal class StorageIterator : IIterator
    {
        private readonly IEnumerator<KeyValuePair<StorageKey, StorageItem>> enumerator;

        public StorageIterator(IEnumerator<KeyValuePair<StorageKey, StorageItem>> enumerator) =>
            this.enumerator = enumerator;

        public void Dispose() => this.enumerator.Dispose();

        public StackItem Key() => this.enumerator.Current.Key.Key;

        public bool Next() => this.enumerator.MoveNext();

        public StackItem Value() => this.enumerator.Current.Value.Value;
    }
}
