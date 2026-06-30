namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Types of donor conference selections.
    /// </summary>
    public enum DonorSelectionType : byte
    {
        /// <summary>No selection; fail-closed sentinel for zero-initialized stale requests.</summary>
        None = 0,

        /// <summary>Select monetary funds package.</summary>
        Funds = 1,

        /// <summary>Select power infrastructure package.</summary>
        Power = 2,

        /// <summary>Select air defense package.</summary>
        Defense = 3
    }
}
