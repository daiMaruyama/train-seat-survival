namespace TrainSurvival.Core
{
    /// <summary>
    /// 小さくて完全に決定論的な疑似乱数生成器。
    /// 同じ seed なら、どのプラットフォームでも必ず同じ列を返す。これにより：
    ///  - シミュレーションを固定の期待値でユニットテストでき、
    ///  - 「今日の通勤」SEED をデイリーチャレンジ／ランキングで共有できる。
    /// 実装：64bit LCG（定数は Knuth の MMIX）＋ xorshift で出力を撹拌し、下位ビットもよく混ぜる。
    /// </summary>
    public sealed class DeterministicRng
    {
        private ulong _state;

        public DeterministicRng(int seed)
        {
            // state が 0 にならないよう、seed を 64bit 全体へ畳み込む。
            _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 1UL;
        }

        private ulong NextUInt64()
        {
            _state = _state * 6364136223846793005UL + 1442695040888963407UL;
            ulong x = _state;
            x ^= x >> 33;
            x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33;
            return x;
        }

        /// <summary>[minInclusive, maxExclusive) の範囲の int を返す。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt64() % range);
        }
    }
}
