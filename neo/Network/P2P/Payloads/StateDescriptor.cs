using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Persistence;

namespace Neo.Network.P2P.Payloads
{
    public class StateDescriptor : ISerializable
    {
        public StateType Type { get; private set; }

        public byte[] Key { get; private set; }

        public string Field { get; private set; }

        public byte[] Value { get; private set; }

        public int Size => sizeof(StateType) + this.Key.GetVarSize() + this.Field.GetVarSize() + this.Value.GetVarSize();

        public Fixed8 SystemFee
        {
            get
            {
                switch (this.Type)
                {
                    case StateType.Validator:
                        return this.GetSystemFeeValidator();
                    default:
                        return Fixed8.Zero;
                }
            }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Type = (StateType)reader.ReadByte();
            if (!Enum.IsDefined(typeof(StateType), this.Type))
            {
                throw new FormatException();
            }

            this.Key = reader.ReadVarBytes(100);
            this.Field = reader.ReadVarString(32);
            this.Value = reader.ReadVarBytes(65535);
            switch (this.Type)
            {
                case StateType.Account:
                    this.CheckAccountState();
                    break;
                case StateType.Validator:
                    this.CheckValidatorState();
                    break;
            }
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write((byte)this.Type);
            writer.WriteVarBytes(this.Key);
            writer.WriteVarString(this.Field);
            writer.WriteVarBytes(this.Value);
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["type"] = this.Type;
            json["key"] = this.Key.ToHexString();
            json["field"] = this.Field;
            json["value"] = this.Value.ToHexString();
            return json;
        }

        internal bool Verify(Snapshot snapshot)
        {
            switch (this.Type)
            {
                case StateType.Account:
                    return this.VerifyAccountState(snapshot);
                case StateType.Validator:
                    return this.VerifyValidatorState();
                default:
                    return false;
            }
        }
        
        private void CheckAccountState()
        {
            if (this.Key.Length != 20)
            {
                throw new FormatException();
            }

            if (this.Field != "Votes")
            {
                throw new FormatException();
            }
        }

        private void CheckValidatorState()
        {
            if (this.Key.Length != 33)
            {
                throw new FormatException();
            }

            if (this.Field != "Registered")
            {
                throw new FormatException();
            }
        }

        private Fixed8 GetSystemFeeValidator()
        {
            switch (this.Field)
            {
                case "Registered":
                    if (this.Value.Any(p => p != 0))
                    {
                        return Fixed8.FromDecimal(1000);
                    }
                    else
                    {
                        return Fixed8.Zero;
                    }

                default:
                    throw new InvalidOperationException();
            }
        }

        private bool VerifyAccountState(Snapshot snapshot)
        {
            switch (this.Field)
            {
                case "Votes":
                    ECPoint[] publicKeys;
                    try
                    {
                        publicKeys = this.Value.AsSerializableArray<ECPoint>((int)Blockchain.MaxValidators);
                    }
                    catch (FormatException)
                    {
                        return false;
                    }

                    var hash = new UInt160(this.Key);
                    var accountState = snapshot.Accounts.TryGet(hash);
                    if (accountState?.IsFrozen != false)
                    {
                        return false;
                    }

                    if (publicKeys.Length > 0)
                    {
                        var neoBalance = accountState.GetBalance(Blockchain.GoverningToken.Hash);
                        if (neoBalance.Equals(Fixed8.Zero))
                        {
                            return false;
                        }

                        var standbyValidators = new HashSet<ECPoint>(Blockchain.StandbyValidators);
                        foreach (var publicKey in publicKeys)
                        {
                            if (!standbyValidators.Contains(publicKey)
                                && snapshot.Validators.TryGet(publicKey)?.Registered != true)
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                default:
                    return false;
            }
        }

        private bool VerifyValidatorState()
        {
            switch (this.Field)
            {
                case "Registered":
                    return true;
                default:
                    return false;
            }
        }
    }
}
