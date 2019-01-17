using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.Extensions;
using Neo.IO;

namespace Neo.Network.P2P.Payloads
{
    public class InvPayload : ISerializable
    {
        public const int MaxHashesCount = 500;

        public InventoryType Type { get; private set; }

        public UInt256[] Hashes { get; private set; }

        public int Size => sizeof(InventoryType) + this.Hashes.GetVarSize();

        public static InvPayload Create(InventoryType type, params UInt256[] hashes) =>
            new InvPayload { Type = type, Hashes = hashes };

        public static IEnumerable<InvPayload> CreateMany(InventoryType type, UInt256[] hashes)
        {
            for (int i = 0; i < hashes.Length; i += MaxHashesCount)
            {
                yield return new InvPayload
                {
                    Type = type,
                    Hashes = hashes.Skip(i).Take(MaxHashesCount).ToArray()
                };
            }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            this.Type = (InventoryType)reader.ReadByte();
            if (!Enum.IsDefined(typeof(InventoryType), this.Type))
            {
                throw new FormatException();
            }

            this.Hashes = reader.ReadSerializableArray<UInt256>(MaxHashesCount);
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write((byte)this.Type);
            writer.Write(this.Hashes);
        }
    }
}
