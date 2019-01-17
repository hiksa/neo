using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.VM;

namespace Neo.Ledger.States
{
    public class AccountState : StateBase, ICloneable<AccountState>
    {
        public UInt160 ScriptHash;
        public bool IsFrozen;
        public ECPoint[] Votes;
        public Dictionary<UInt256, Fixed8> Balances;

        public AccountState()
        {
        }

        public AccountState(UInt160 hash)
        {
            this.ScriptHash = hash;
            this.IsFrozen = false;
            this.Votes = new ECPoint[0];
            this.Balances = new Dictionary<UInt256, Fixed8>();
        }

        public override int Size => base.Size + this.ScriptHash.Size + sizeof(bool)
            + this.Votes.GetVarSize() + IO.Helper.GetVarSize(this.Balances.Count)
            + (this.Balances.Count * (32 + 8));

        AccountState ICloneable<AccountState>.Clone()
        {
            return new AccountState
            {
                ScriptHash = this.ScriptHash,
                IsFrozen = this.IsFrozen,
                Votes = this.Votes,
                Balances = this.Balances.ToDictionary(p => p.Key, p => p.Value)
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);

            this.ScriptHash = reader.ReadSerializable<UInt160>();
            this.IsFrozen = reader.ReadBoolean();
            this.Votes = new ECPoint[reader.ReadVarInt()];

            for (int i = 0; i < this.Votes.Length; i++)
            {
                this.Votes[i] = ECPoint.DeserializeFrom(reader, ECCurve.Secp256r1);
            }

            var count = (int)reader.ReadVarInt();
            this.Balances = new Dictionary<UInt256, Fixed8>(count);

            for (int i = 0; i < count; i++)
            {
                var assetId = reader.ReadSerializable<UInt256>();
                var value = reader.ReadSerializable<Fixed8>();
                this.Balances.Add(assetId, value);
            }
        }

        void ICloneable<AccountState>.FromReplica(AccountState replica)
        {
            this.ScriptHash = replica.ScriptHash;
            this.IsFrozen = replica.IsFrozen;
            this.Votes = replica.Votes;
            this.Balances = replica.Balances;
        }

        public Fixed8 GetBalance(UInt256 assetId)
        {
            if (!this.Balances.TryGetValue(assetId, out Fixed8 value))
            {
                value = Fixed8.Zero;
            }

            return value;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(this.ScriptHash);
            writer.Write(this.IsFrozen);
            writer.Write(this.Votes);

            var balances = this.Balances.Where(p => p.Value > Fixed8.Zero).ToArray();
            writer.WriteVarInt(balances.Length);

            foreach (var pair in balances)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
        }

        public override JObject ToJson()
        {
            var json = base.ToJson();
            json["script_hash"] = this.ScriptHash.ToString();
            json["frozen"] = this.IsFrozen;
            json["votes"] = new JArray(this.Votes.Select(p => (JObject)p.ToString()));
            var balances = this.Balances.Select(p =>
            {
                var balance = new JObject();
                balance["asset"] = p.Key.ToString();
                balance["value"] = p.Value.ToString();
                return balance;
            });

            json["balances"] = new JArray(balances);

            return json;
        }
    }
}
