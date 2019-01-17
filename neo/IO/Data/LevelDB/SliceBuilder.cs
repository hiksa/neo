using System;
using System.Collections.Generic;
using System.Text;

namespace Neo.IO.Data.LevelDB
{
    public class SliceBuilder
    {
        private List<byte> data = new List<byte>();

        private SliceBuilder()
        {
        }

        public SliceBuilder Add(byte value)
        {
            this.data.Add(value);
            return this;
        }

        public SliceBuilder Add(ushort value)
        {
            this.data.AddRange(BitConverter.GetBytes(value));
            return this;
        }

        public SliceBuilder Add(uint value)
        {
            this.data.AddRange(BitConverter.GetBytes(value));
            return this;
        }

        public SliceBuilder Add(long value)
        {
            this.data.AddRange(BitConverter.GetBytes(value));
            return this;
        }

        public SliceBuilder Add(IEnumerable<byte> value)
        {
            this.data.AddRange(value);
            return this;
        }

        public SliceBuilder Add(string value)
        {
            this.data.AddRange(Encoding.UTF8.GetBytes(value));
            return this;
        }

        public SliceBuilder Add(ISerializable value)
        {
            this.data.AddRange(value.ToArray());
            return this;
        }

        public static SliceBuilder Begin() => new SliceBuilder();

        public static SliceBuilder Begin(byte prefix) => new SliceBuilder().Add(prefix);

        public static implicit operator Slice(SliceBuilder value) => value.data.ToArray();
    }
}
