using System;
using Neo.VM;

namespace Neo.SmartContract.Enumerators
{
    internal interface IEnumerator : IDisposable
    {
        bool Next();

        StackItem Value();
    }
}
