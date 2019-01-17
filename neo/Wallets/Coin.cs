using System;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Wallets
{
    public class Coin : IEquatable<Coin>
    {
        private string address = null;

        public CoinReference Reference { get; set; }

        public TransactionOutput Output { get; set; }

        public CoinStates State { get; set; }

        public string Address
        {
            get
            {
                if (this.address == null)
                {
                    this.address = this.Output.ScriptHash.ToAddress();
                }

                return this.address;
            }
        }

        public bool Equals(Coin other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return this.Reference.Equals(other.Reference);
        }

        public override bool Equals(object obj) => this.Equals(obj as Coin);

        public override int GetHashCode() => this.Reference.GetHashCode();
    }
}
