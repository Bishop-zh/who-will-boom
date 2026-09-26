using System;

namespace BombTavern.Core
{
    /// <summary>
    /// 确定性随机数（xorshift128+，SplitMix64 播种）。
    /// 联机模式下所有随机都从同一个种子推导，保证各端模拟结果完全一致。
    /// 禁止在逻辑层使用 UnityEngine.Random / Time / 浮点超越函数。
    /// </summary>
    [Serializable]
    public struct DeterministicRandom
    {
        ulong _s0;
        ulong _s1;

        const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        const float Inv24Bit = 1f / 16777216f;   // 2^-24，float 可精确表示

        public DeterministicRandom(ulong seed)
        {
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            if (_s0 == 0UL && _s1 == 0UL) _s1 = GoldenGamma;
        }

        static ulong SplitMix64(ref ulong x)
        {
            x += GoldenGamma;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>[0,1) 均匀分布，使用 24 位尾数以保证跨平台位精确。</summary>
        public float NextFloat() => (NextULong() >> 40) * Inv24Bit;

        /// <summary>[minInclusive, maxExclusive)，无模偏置。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint r;
            do { r = NextUInt(); } while (r >= limit);
            return minInclusive + (int)(r % range);
        }

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>按概率掷骰。</summary>
        public bool Roll(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }

        /// <summary>近似正态分布 [-1,1]，中心峰（两个均匀分布之和减 1）。</summary>
        public float NextCentered() => NextFloat() + NextFloat() - 1f;

        public DeterministicRandom Fork(ulong salt)
        {
            var copy = this;
            copy._s0 ^= SplitMix64(ref salt);
            copy._s1 ^= SplitMix64(ref salt);
            return copy;
        }
    }
}
