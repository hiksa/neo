using System.Collections;
using System.Linq;

namespace Neo.Cryptography
{
    public class BloomFilter
    {
        private readonly uint[] seeds;
        private readonly BitArray bits;

        public BloomFilter(int m, int k, uint nTweak, byte[] elements = null)
        {
            this.seeds = Enumerable
                .Range(0, k)
                .Select(p => ((uint)p * 0xFBA4C795) + nTweak)
                .ToArray();

            this.bits = elements == null 
                ? new BitArray(m) 
                : new BitArray(elements);

            this.bits.Length = m;
            this.Tweak = nTweak;
        }

        public int K => this.seeds.Length;

        public int M => this.bits.Length;

        public uint Tweak { get; private set; }

        public void Add(byte[] element)
        {
            var hashedSeeds = this.seeds
                .AsParallel()
                .Select(s => element.Murmur32(s));

            foreach (var item in hashedSeeds)
            {
                var bitIndex = (int)(item % (uint)this.bits.Length);
                this.bits.Set(bitIndex, true);
            }
        }

        public bool Check(byte[] element)
        {
            var hashedSeeds = this.seeds
                .AsParallel()
                .Select(s => element.Murmur32(s));

            foreach (var item in hashedSeeds)
            {
                if (!this.bits.Get((int)(item % (uint)this.bits.Length)))
                {
                    return false;
                }
            }

            return true;
        }

        public void GetBits(byte[] newBits) => this.bits.CopyTo(newBits, 0);
    }
}
