using System;
using System.IO;

namespace Neo.Network.P2P.Payloads
{
    public class ContractTransaction : Transaction
    {
        public ContractTransaction()
            : base(TransactionType.ContractTransaction)
        {
        }

        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            if (this.Version != 0)
            {
                throw new FormatException();
            }
        }
    }
}
