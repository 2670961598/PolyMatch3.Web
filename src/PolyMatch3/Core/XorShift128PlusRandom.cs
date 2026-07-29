using System;

namespace PolyMatch3.Core
{
    /// <summary>
    /// xorshift128+ 确定性随机数发生器（参考实现，约 40 行，零依赖）。
    /// 同种子必然产生同序列；状态仅两个 ulong，可随时快照/恢复。
    /// 取模法映射区间（存在可忽略的模偏差，游戏场景无感）。
    /// </summary>
    public sealed class XorShift128PlusRandom : IRandom
    {
        private ulong _s0;
        private ulong _s1;
        private int _cursor;

        public XorShift128PlusRandom(ulong seed)
        {
            // splitmix64 展开种子，避免全零状态
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 0x9E3779B97F4A7C15UL;
        }

        public int Cursor => _cursor;

        public ulong State0 { get => _s0; set => _s0 = value; }
        public ulong State1 { get => _s1; set => _s1 = value; }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextUInt64() % (ulong)maxExclusive);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + (int)(NextUInt64() % (ulong)(maxExclusive - minInclusive));
        }

        private ulong NextUInt64()
        {
            _cursor++;
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        private static ulong SplitMix64(ref ulong state)
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
