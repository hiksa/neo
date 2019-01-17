using System;
using System.Collections.Generic;
using Neo.VM;

namespace Neo.SmartContract.Iterators
{
    internal class ArrayWrapper : IIterator
    {
        private readonly IList<StackItem> array;
        private int index = -1;

        public ArrayWrapper(IList<StackItem> array)
        {
            this.array = array;
        }

        public void Dispose()
        {
        }

        public StackItem Key()
        {
            if (this.index < 0)
            {
                throw new InvalidOperationException();
            }

            return this.index;
        }

        public bool Next()
        {
            var next = this.index + 1;
            if (next >= this.array.Count)
            {
                return false;
            }

            this.index = next;
            return true;
        }

        public StackItem Value()
        {
            if (this.index < 0)
            {
                throw new InvalidOperationException();
            }

            return this.array[this.index];
        }
    }
}
