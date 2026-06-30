using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Dedup helper for DayChangedEvent subscribers.
    /// Ensures each day is processed exactly once, even if event fires multiple times.
    ///
    /// Replaces the copy-paste pattern:
    ///   if (evt.DayNumber &lt;= m_LastProcessedDay) return;
    ///   m_LastProcessedDay = evt.DayNumber;
    ///
    /// Usage in OnDayChanged handler:
    ///   if (m_DayDedup.AlreadyProcessed(evt.DayNumber)) return;
    ///
    /// Serialization (call from .Serialization.cs):
    ///   m_DayDedup.Serialize(writer);
    ///   m_DayDedup.Deserialize(reader);
    ///   m_DayDedup.Reset(); // SetDefaults
    ///
    /// Binary-compatible: writes/reads the same int as the old m_LastProcessedDay pattern.
    /// </summary>
    public struct DayChangedDedup
    {
        private int m_LastProcessedDay;

        /// <summary>
        /// Returns true if this day was already processed. Updates tracking if new day.
        /// Consistent &lt;= semantics: day N processed once, re-delivery of day N is skipped.
        /// </summary>
        public bool AlreadyProcessed(int dayNumber)
        {
            if (dayNumber <= m_LastProcessedDay) return true;
            m_LastProcessedDay = dayNumber;
            return false;
        }

        /// <summary>Check-only variant for systems that defer the actual daily work.</summary>
        public bool IsProcessed(int dayNumber) => dayNumber <= m_LastProcessedDay;

        /// <summary>Mark a day after deferred work has completed successfully.</summary>
        public void MarkProcessed(int dayNumber)
        {
            if (dayNumber > m_LastProcessedDay)
                m_LastProcessedDay = dayNumber;
        }

        /// <summary>Reset to initial state (new game / act transition).</summary>
        public void Reset() => SetDefaults();

        /// <summary>Set initial valid state.</summary>
        public void SetDefaults() => m_LastProcessedDay = 0;

        /// <summary>Restore from keyed deserialization (replaces Deserialize for keyed format).</summary>
        public static DayChangedDedup FromSave(int lastProcessedDay) =>
            new DayChangedDedup { m_LastProcessedDay = DayChangedDedupCodec.NormalizeFromSave(lastProcessedDay) };

        /// <summary>Last successfully processed day number (0 = none).</summary>
        public int LastProcessedDay => m_LastProcessedDay;

        /// <summary>Write dedup state. Binary-compatible with old writer.Write(m_LastProcessedDay).</summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
            => DayChangedDedupCodec.Write(m_LastProcessedDay, writer);

        /// <summary>Read dedup state. Binary-compatible with old reader.Read(out m_LastProcessedDay).</summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
            => m_LastProcessedDay = DayChangedDedupCodec.Read(reader);
    }
}
