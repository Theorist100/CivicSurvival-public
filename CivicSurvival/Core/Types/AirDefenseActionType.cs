namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of air defense actions.
    /// </summary>
    public enum AirDefenseActionType : byte
    {
        None = 0,

        /// <summary>SBU visit (Valera operation).</summary>
        PerformSBUVisit = 2,

        /// <summary>Emergency evacuation operation.</summary>
        PerformEvacuation = 3,

        /// <summary>Toggle counter-OSINT measures.</summary>
        ToggleCounterOSINT = 4,

        /// <summary>Recurring Counter-OSINT upkeep charge.</summary>
        CounterOSINTDailyCost = 6
    }
}
