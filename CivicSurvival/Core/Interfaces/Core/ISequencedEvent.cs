namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Event with a natural monotonic sequence number.
    /// EventBus uses this to automatically skip duplicate deliveries:
    /// if a handler already processed sequence N, any publish with sequence ≤ N is skipped.
    ///
    /// This eliminates the need for manual dedup guards (e.g., DayChangedDedup)
    /// in each subscriber — the EventBus enforces it centrally.
    ///
    /// Examples of natural sequences:
    /// - DayChangedEvent: DayNumber (1, 2, 3, ...)
    /// - WarDayChangedEvent: WarDay (0, 1, 2, ...)
    ///
    /// Events without natural ordering (one-shot signals like WaveEndedEvent,
    /// or lifecycle replays like ActChangedEvent after load) should NOT
    /// implement this — they are not watermarked.
    /// </summary>
    public interface ISequencedEvent : IGameEvent
    {
        /// <summary>
        /// Monotonically increasing sequence number.
        /// EventBus skips delivery when Sequence ≤ subscriber's last handled sequence.
        /// </summary>
        long Sequence { get; }
    }
}
