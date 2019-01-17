using Neo.Network.P2P.Payloads;

namespace Neo.IO.Caching
{
    internal class RelayCache : FIFOCache<UInt256, IInventory>
    {
        public RelayCache(int maxCapacity)
            : base(maxCapacity)
        {
        }

        protected override UInt256 GetKeyForItem(IInventory item) => item.Hash;
    }
}
