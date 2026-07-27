namespace CivicSurvival.Core.Types.Snapshots
{
    /// <summary>
    /// Immutable snapshot of game time state.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public readonly struct GameTimeSnapshot
    {
        /// <summary>Current hour within day (0-24).</summary>
        public readonly float CurrentHour;

        /// <summary>Normalized time within day (0-1).</summary>
        public readonly float NormalizedTime;

        /// <summary>Current game day number (increments at midnight).</summary>
        public readonly int CurrentDay;

        /// <summary>Total game hours since start.</summary>
        public readonly float TotalGameHours;

        /// <summary>Whether war has started.</summary>
        public readonly bool IsWarStarted;

        /// <summary>Current war day (0 = first day of war, -1 if war not started).</summary>
        public readonly int WarDay;

        public static GameTimeSnapshot NotStarted => new(0f, 0f, 0, 0f, isWarStarted: false, warDay: -1);

        public GameTimeSnapshot(
            float currentHour,
            float normalizedTime,
            int currentDay,
            float totalGameHours,
            bool isWarStarted,
            int warDay)
        {
            CurrentHour = currentHour;
            NormalizedTime = normalizedTime;
            CurrentDay = currentDay;
            TotalGameHours = totalGameHours;
            IsWarStarted = isWarStarted;
            WarDay = warDay;
        }
    }
}
