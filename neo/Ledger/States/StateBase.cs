using System;
using System.IO;
using Neo.IO;
using Neo.IO.Json;

namespace Neo.Ledger.States
{
    public abstract class StateBase : ISerializable
    {
        public const byte StateVersion = 0;

        public virtual int Size => sizeof(byte);

        public virtual void Deserialize(BinaryReader reader)
        {
            if (reader.ReadByte() != StateBase.StateVersion)
            {
                throw new FormatException();
            }
        }

        public virtual void Serialize(BinaryWriter writer) => 
            writer.Write(StateBase.StateVersion);

        public virtual JObject ToJson()
        {
            var json = new JObject();
            json["version"] = StateBase.StateVersion;
            return json;
        }
    }
}
