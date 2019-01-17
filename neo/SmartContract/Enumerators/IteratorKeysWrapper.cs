﻿using Neo.SmartContract.Iterators;
using Neo.VM;

namespace Neo.SmartContract.Enumerators
{
    internal class IteratorKeysWrapper : IEnumerator
    {
        private readonly IIterator iterator;

        public IteratorKeysWrapper(IIterator iterator)
        {
            this.iterator = iterator;
        }

        public void Dispose() => this.iterator.Dispose();

        public bool Next() => this.iterator.Next();

        public StackItem Value() => this.iterator.Key();
    }
}
