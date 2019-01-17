using System.Collections.Generic;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins
{
    public interface IPolicyPlugin
    {
        bool FilterForMemoryPool(Transaction tx);

        IEnumerable<Transaction> FilterForBlock(IEnumerable<Transaction> transactions);
    }
}
