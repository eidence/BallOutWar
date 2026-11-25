using System;

namespace BOW.Simulation
{
    /// <summary>
    /// Small deterministic xorshift32 generator. Its state lives inside <see cref="MatchState"/>,
    /// so snapshots, offline resume and replays reproduce the same random sequence.
    /// </summary>
    [Serializable]
    public struct SimRandom
    {
        public uint State;

        public SimRandom(uint seed)
        {
            State = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = State;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            State = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }
    }
}
