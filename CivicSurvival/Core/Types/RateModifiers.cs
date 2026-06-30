using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Single entry in a rate modifier registry: source key + multiplier value.
    /// </summary>
    public struct ModifierEntry
    {
        public byte Source;
        public float Value;
    }

    /// <summary>
    /// Collects named multiplier sources, resolves them into a single clamped value.
    /// Eliminates death spirals from uncoordinated multiplier stacking.
    ///
    /// Each gameplay rate category wraps this in an IComponentData singleton.
    /// Producers call Set() with their source key, consumers call Resolve(baseRate).
    ///
    /// Supports:
    /// - Multiplicative compound: baseRate × mod1 × mod2 × ... (clamped to [Floor, Ceiling])
    /// - Override: bypasses all multipliers, still clamped
    /// - Per-source read-back for debug/UI
    ///
    /// Transient — not serialized. Systems recompute from game state each update.
    /// First-frame (no entries): Resolve(baseRate) returns clamp(baseRate, Floor, Ceiling).
    /// </summary>
    public struct RateModifiers
    {
        private const float ZERO_BOUNDS_EPSILON = 0.000001f;
        private static readonly LogContext Log = new("RateModifiers");

#pragma warning disable S3459 // FixedList is value-type with internal fixed buffer — initialized to empty by default
        private FixedList128Bytes<ModifierEntry> m_Entries;
#pragma warning restore S3459
        private float m_Floor;
        private float m_Ceiling;
        private byte m_Initialized;
        private byte m_HasOverride;
        private float m_OverrideValue;

        public float Floor => m_Floor;
        public float Ceiling => m_Ceiling;

        /// <summary>
        /// Create a new RateModifiers with the given floor and ceiling.
        /// </summary>
        public static RateModifiers Create(float floor, float ceiling)
        {
            if (!IsFinite(floor) || !IsFinite(ceiling) || floor > ceiling)
            {
                Log.Warn($"Invalid bounds floor={floor}, ceiling={ceiling}; using pass-through bounds");
                floor = 0f;
                ceiling = 1f;
            }

            return new RateModifiers
            {
                m_Floor = floor,
                m_Ceiling = ceiling,
                m_Initialized = 1
            };
        }

        /// <summary>
        /// Set (upsert) a modifier for the given source.
        /// If source already exists, updates its value. Otherwise, adds a new entry.
        /// </summary>
        public void Set(byte source, float multiplier)
        {
            if (!IsFinite(multiplier))
            {
                Log.Warn($"Rejected non-finite multiplier from source={source}: {multiplier}");
                return;
            }

            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].Source == source)
                {
                    m_Entries[i] = new ModifierEntry { Source = source, Value = multiplier };
                    return;
                }
            }

            // FIX S30_RAG2:8: Guard FixedList capacity — overflow throws InvalidOperationException
            if (m_Entries.Length >= m_Entries.Capacity)
            {
                Log.Warn($"Capacity {m_Entries.Capacity} exhausted; dropped source={source}, multiplier={multiplier:F3}");
                return;
            }
            m_Entries.Add(new ModifierEntry { Source = source, Value = multiplier });
        }

        /// <summary>
        /// Set an override that bypasses all multipliers.
        /// The override value is still clamped to [Floor, Ceiling].
        /// </summary>
        public void SetOverride(float finalRate)
        {
            if (!IsFinite(finalRate))
            {
                Log.Warn($"Rejected non-finite override: {finalRate}");
                return;
            }

            m_HasOverride = 1;
            m_OverrideValue = finalRate;
        }

        /// <summary>
        /// Clear the override — Resolve will use compound multipliers again.
        /// </summary>
        public void ClearOverride()
        {
            m_HasOverride = 0;
        }

        /// <summary>
        /// Resolve the final rate.
        /// If override is set: clamp(override, Floor, Ceiling).
        /// Otherwise: clamp(baseRate × mod1 × mod2 × ..., Floor, Ceiling).
        /// </summary>
        public float Resolve(float baseRate)
        {
            if (IsDefaultUninitialized())
                return IsFinite(baseRate) ? baseRate : 0f;

            if (m_HasOverride != 0)
                return ClampFinite(m_OverrideValue);

            float compound = IsFinite(baseRate) ? baseRate : m_Floor;
            for (int i = 0; i < m_Entries.Length; i++)
            {
                compound *= m_Entries[i].Value;
                if (!IsFinite(compound))
                    return m_Floor;
            }

            return ClampFinite(compound);
        }

        /// <summary>
        /// Read a specific modifier value (for debug/UI). Returns 1.0 if source not found.
        /// </summary>
        public float GetModifier(byte source)
        {
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].Source == source)
                    return m_Entries[i].Value;
            }

            return 1.0f;
        }

        /// <summary>Number of registered modifier sources.</summary>
        public int Count => m_Entries.Length;

        /// <summary>Whether an override is active.</summary>
        public bool HasOverride => m_HasOverride != 0;

        private bool IsDefaultUninitialized()
        {
            return m_Initialized == 0
                && math.abs(m_Floor) < ZERO_BOUNDS_EPSILON
                && math.abs(m_Ceiling) < ZERO_BOUNDS_EPSILON
                && m_Entries.Length == 0
                && m_HasOverride == 0;
        }

        private float ClampFinite(float value)
        {
            return IsFinite(value) ? math.clamp(value, m_Floor, m_Ceiling) : m_Floor;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
