using Neo.VM;

namespace Neo.SmartContract.Enumerators
{
    internal class ConcatenatedEnumerator : IEnumerator
    {
        private readonly IEnumerator first, second;
        private IEnumerator current;

        public ConcatenatedEnumerator(IEnumerator first, IEnumerator second)
        {
            this.current = this.first = first;
            this.second = second;
        }

        public void Dispose()
        {
            this.first.Dispose();
            this.second.Dispose();
        }

        public bool Next()
        {
            if (this.current.Next())
            {
                return true;
            }

            this.current = this.second;
            return this.current.Next();
        }

        public StackItem Value() => this.current.Value();
    }
}
