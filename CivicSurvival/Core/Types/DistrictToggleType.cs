namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of district toggle operations.
    /// </summary>
    public enum DistrictToggleType : byte
    {
        None = 0,

        /// <summary>Toggle manual blackout state for district.</summary>
        Blackout = 1,

        /// <summary>Toggle specific building category on/off.</summary>
        Category = 2,

        /// <summary>Toggle VIP priority status.</summary>
        VIP = 3,

        /// <summary>Toggle VIP bypass restrictions.</summary>
        VIPBypass = 4,

        /// <summary>Set district power schedule preset.</summary>
        Schedule = 5,

        /// <summary>Idempotently set manual blackout state for district.</summary>
        SetBlackout = 6
    }
}
