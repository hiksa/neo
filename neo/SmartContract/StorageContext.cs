namespace Neo.SmartContract
{
    internal class StorageContext
    {
        public UInt160 ScriptHash { get; set; }

        public bool IsReadOnly { get; set; }

        public byte[] ToArray() => this.ScriptHash.ToArray();
    }
}
