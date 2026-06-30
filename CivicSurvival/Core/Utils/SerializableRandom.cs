using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Serializable pseudo-random number generator.
    /// Uses Xorshift64 algorithm — fast, good statistical properties, single ulong state.
    ///
    /// Use this instead of System.Random when state must persist across save/load.
    /// For thread-safe static random, use ThreadSafeRandom instead.
    ///
    /// Usage:
    /// <code>
    /// // In system class
    /// private SerializableRandom m_Random = new(12345);
    ///
    /// // In OnUpdate
    /// float roll = m_Random.NextFloat();
    /// if (roll &lt; 0.3f) { ... }
    ///
    /// // In Serialize
    /// m_Random.Serialize(writer);
    ///
    /// // In Deserialize
    /// m_Random.Deserialize(reader);
    /// </code>
    /// </summary>
    public struct SerializableRandom
    {
        private ulong m_State;

        /// <summary>
        /// Create with explicit seed.
        /// Note: Negative seeds are valid - they're cast to ulong (two's complement),
        /// resulting in large positive values. This is intentional and produces
        /// good random sequences. Zero is converted to 1 (Xorshift requires non-zero).
        /// </summary>
        public SerializableRandom(int seed)
        {
            // Ensure non-zero state (Xorshift requires non-zero)
            // Negative seeds become large positive ulong values (valid)
            m_State = seed == 0 ? 1UL : (ulong)seed;
        }

        /// <summary>
        /// Create with explicit ulong seed (for deserialization).
        /// </summary>
        public SerializableRandom(ulong state)
        {
            m_State = state == 0 ? 1UL : state;
        }

        /// <summary>
        /// Current internal state (for debugging/logging).
        /// </summary>
        public ulong State => m_State;

        /// <summary>
        /// Generate next random ulong.
        /// </summary>
        private ulong NextULong()
        {
            // Xorshift64 algorithm
            m_State ^= m_State << 13;
            m_State ^= m_State >> 7;
            m_State ^= m_State << 17;
            return m_State;
        }

        private ulong NextBoundedULong(ulong maxExclusive)
        {
            if (maxExclusive <= 1UL) return 0UL;

            ulong threshold = (0UL - maxExclusive) % maxExclusive;
            ulong result;
            do
            {
                result = NextULong();
            }
            while (result < threshold);

            return result % maxExclusive;
        }

        /// <summary>
        /// Generate random int in range [0, int.MaxValue).
        /// </summary>
        public int Next()
        {
            // Mask to 31 bits to get non-negative int
            return (int)(NextULong() & 0x7FFFFFFF);
        }

        /// <summary>
        /// Generate random int in range [0, maxExclusive).
        /// Uses rejection sampling to avoid modulo bias.
        /// </summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            if (maxExclusive == 1) return 0;

            // Use 32-bit range for efficiency
            uint max = (uint)maxExclusive;
            uint threshold = RejectionThreshold(max);

            // Rejection sampling: discard values that would cause bias
            uint result;
            do
            {
                result = (uint)NextULong();
            }
            while (result < threshold);

            return (int)(result % max);
        }

        /// <summary>
        /// Generate random int in range [minInclusive, maxExclusive).
        /// </summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            long range = (long)maxExclusive - minInclusive;
            return (int)(minInclusive + (long)NextBoundedULong((ulong)range));
        }

        internal static uint RejectionThreshold(uint maxExclusive)
        {
            return maxExclusive <= 1U ? 0U : unchecked(0U - maxExclusive) % maxExclusive;
        }

        /// <summary>
        /// Generate random double in range [0.0, 1.0).
        /// </summary>
        public double NextDouble()
        {
            return (NextULong() & 0x1FFFFFFFFFFFFF) / (double)0x20000000000000;
        }

        /// <summary>
        /// Generate random float in range [0.0f, 1.0f).
        /// </summary>
        public float NextFloat()
        {
            return (float)NextDouble();
        }

        /// <summary>
        /// Generate random float in range [0.0f, max).
        /// </summary>
        public float NextFloat(float max)
        {
            return NextFloat() * max;
        }

        /// <summary>
        /// Generate random float in range [min, max).
        /// </summary>
        public float NextFloat(float min, float max)
        {
            return min + NextFloat() * (max - min);
        }

        /// <summary>
        /// Return true with given probability [0.0 - 1.0].
        /// </summary>
        public bool Chance(double probability)
        {
            return NextDouble() < probability;
        }

        /// <summary>
        /// Serialize state to writer.
        /// </summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_State);
        }

        /// <summary>
        /// Deserialize state from reader.
        /// </summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            try
            {
                reader.Read(out m_State);
                // Ensure non-zero
                if (m_State == 0) m_State = 1;
            }
            catch (System.Exception ex)
            {
                // Internal utility, using global default log context
                LogContext.Default.Error($"Deserialize {nameof(SerializableRandom)} failed: {ex}");
                m_State = 1;
                throw;
            }
        }
    }
}
