using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Ledger.States
{
    public class UnspentCoinState : StateBase, ICloneable<UnspentCoinState>
    {
        public CoinStates[] Items;

        public override int Size => base.Size + this.Items.GetVarSize();

        UnspentCoinState ICloneable<UnspentCoinState>.Clone()
        {
            return new UnspentCoinState
            {
                Items = (CoinStates[])this.Items.Clone()
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.Items = reader.ReadVarBytes().Select(p => (CoinStates)p).ToArray();
        }

        void ICloneable<UnspentCoinState>.FromReplica(UnspentCoinState replica)
        {
            this.Items = replica.Items;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.WriteVarBytes(this.Items.Cast<byte>().ToArray());
        }
    }
}
