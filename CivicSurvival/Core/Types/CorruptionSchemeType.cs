namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of corruption scheme percentage settings.
    /// </summary>
    public enum CorruptionSchemeType : byte
    {
        None = 0,

        /// <summary>Emergency fund withdrawal percentage.</summary>
        EmergencyFundWithdraw = 1,

        /// <summary>Fuel siphoning percentage.</summary>
        FuelSiphon = 2,

        /// <summary>Draft-deferral sale rate percentage (corrupt military registration office).</summary>
        DraftDeferral = 3,

        /// <summary>Construction kickback percentage skimmed from road/net construction spend.</summary>
        ConstructionKickback = 4
    }
}
