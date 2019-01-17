using System;
using Neo.Network.P2P.Payloads;

namespace Neo.Ledger
{
    public class PoolItem : IComparable<PoolItem>
    {
        public readonly Transaction Transaction;
        public readonly DateTime Timestamp;
        public DateTime LastBroadcastTimestamp;

        public PoolItem(Transaction tx)
        {
            this.Transaction = tx;
            this.Timestamp = DateTime.UtcNow;
            this.LastBroadcastTimestamp = this.Timestamp;
        }

        public int CompareTo(Transaction other)
        {
            if (other == null)
            {
                return 1;
            }

            var result = this.Transaction.FeePerByte.CompareTo(other.FeePerByte);
            if (result != 0)
            {
                return result;
            }

            result = this.Transaction.NetworkFee.CompareTo(other.NetworkFee);
            if (result != 0)
            {
                return result;
            }

            return this.Transaction.Hash.CompareTo(other.Hash);
        }

        public int CompareTo(PoolItem otherItem)
        {
            if (otherItem == null)
            {
                return 1;
            }

            return this.CompareTo(otherItem.Transaction);
        }
    }
}
