using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a field for automatic serialization via source generator.
    /// The PersistSerializationGenerator produces SerializePersistFields/DeserializePersistFields/ResetPersistFields
    /// helper methods that include all [Persist] fields — impossible to forget a field.
    ///
    /// Supported types: float, int, bool, byte, long, double, string,
    /// enum (: byte or : int), [Flags] enum (: byte or : int).
    /// For custom types, collections — use manual serialization.
    ///
    /// Float/double bounds (optional):
    /// <code>
    /// [Persist(Min = 0, Max = 100)]           float m_ShockLevel;     // → ReadSafeFloat(0, 100, 0, "m_ShockLevel")
    /// [Persist(Unclamped = true)]              float m_PhaseStartTime; // → ReadSafeFloatUnclamped(0, "m_PhaseStartTime")
    /// [Persist]                                float m_Timer;          // → ReadSafeFloat(0, "m_Timer") — NaN/Inf only
    /// [Persist(Min = 0, Max = 1, Default = 0.5)] float m_Ratio;       // → ReadSafeFloat(0, 1, 0.5, "m_Ratio")
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PersistAttribute : Attribute
    {
        /// <summary>Minimum bound for float/double. Generator emits ReadSafeFloat(min, max, default). NaN = not set.</summary>
        public double Min { get; set; } = double.NaN;

        /// <summary>Maximum bound for float/double. Generator emits ReadSafeFloat(min, max, default). NaN = not set.</summary>
        public double Max { get; set; } = double.NaN;

        /// <summary>Default value used in ReadSafeFloat and ResetPersistFields. NaN = use default(T).</summary>
        public double Default { get; set; } = double.NaN;

        /// <summary>If true, float/double uses ReadSafeFloatUnclamped (NaN/Inf check only, no range bounds).
        /// Use for timestamps, coordinates, velocities — values with no logical range.</summary>
        public bool Unclamped { get; set; }

        /// <summary>
        /// Maximum collection size for List/Dictionary/Array fields (OOM protection).
        /// Generator emits ReadCollectionSize(maxSize), which throws on invalid sizes to avoid stream desync.
        /// 0 = not set (scalar field).
        /// </summary>
        public int MaxCollectionSize { get; set; }
    }
}
