using System.IO;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Ledger.States
{
    public class ValidatorState : StateBase, ICloneable<ValidatorState>
    {
        public ECPoint PublicKey;
        public bool Registered;
        public Fixed8 Votes;

        public ValidatorState()
        {
        }

        public ValidatorState(ECPoint pubkey)
        {
            this.PublicKey = pubkey;
            this.Registered = false;
            this.Votes = Fixed8.Zero;
        }

        public override int Size => base.Size + this.PublicKey.Size + sizeof(bool) + this.Votes.Size;

        ValidatorState ICloneable<ValidatorState>.Clone()
        {
            return new ValidatorState
            {
                PublicKey = this.PublicKey,
                Registered = this.Registered,
                Votes = this.Votes
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.PublicKey = ECPoint.DeserializeFrom(reader, ECCurve.Secp256r1);
            this.Registered = reader.ReadBoolean();
            this.Votes = reader.ReadSerializable<Fixed8>();
        }

        void ICloneable<ValidatorState>.FromReplica(ValidatorState replica)
        {
            this.PublicKey = replica.PublicKey;
            this.Registered = replica.Registered;
            this.Votes = replica.Votes;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(this.PublicKey);
            writer.Write(this.Registered);
            writer.Write(this.Votes);
        }
    }
}
