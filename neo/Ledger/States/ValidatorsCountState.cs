using System.IO;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Ledger.States
{
    public class ValidatorsCountState : StateBase, ICloneable<ValidatorsCountState>
    {
        public Fixed8[] Votes;

        public override int Size => base.Size + Votes.GetVarSize();

        public ValidatorsCountState()
        {
            this.Votes = new Fixed8[Blockchain.MaxValidators];
        }

        ValidatorsCountState ICloneable<ValidatorsCountState>.Clone()
        {
            return new ValidatorsCountState
            {
                Votes = (Fixed8[])Votes.Clone()
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            this.Votes = reader.ReadSerializableArray<Fixed8>();
        }

        void ICloneable<ValidatorsCountState>.FromReplica(ValidatorsCountState replica)
        {
            this.Votes = replica.Votes;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.Votes);
        }
    }
}
