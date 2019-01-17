using System;
using Neo.VM;

namespace Neo.SmartContract
{
    internal class ContainerPlaceholder : StackItem
    {
        public StackItemType Type { get; set; }

        public int ElementCount { get; set; }

        public override bool Equals(StackItem other)
        {
            throw new NotSupportedException();
        }

        public override byte[] GetByteArray()
        {
            throw new NotSupportedException();
        }
    }
}
