using System;
using System.IO;

namespace Neo.IO.Wrappers
{
    public sealed class UInt32Wrapper : SerializableWrapper<uint>, IEquatable<UInt32Wrapper>
    {
        public UInt32Wrapper()
        {
        }

        private UInt32Wrapper(uint value)
        {
            this.value = value;
        }

        public override int Size => sizeof(uint);

        public override void Deserialize(BinaryReader reader) => this.value = reader.ReadUInt32();

        public bool Equals(UInt32Wrapper other) => this.value == other.value;
        
        public override void Serialize(BinaryWriter writer) => writer.Write(this.value);

        public static implicit operator UInt32Wrapper(uint value) => new UInt32Wrapper(value);

        public static implicit operator uint(UInt32Wrapper wrapper) => wrapper.value;        
    }
}
