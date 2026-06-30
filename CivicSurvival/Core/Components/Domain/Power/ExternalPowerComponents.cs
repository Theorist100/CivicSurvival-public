using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Marks the donor generator power source entity.
    /// </summary>
    public struct ExternalPowerDonorTag : IComponentData
    {
    }

    /// <summary>
    /// External power bonus injected by non-grid systems (e.g., donor generators).
    /// PowerGrid reads this without knowing who wrote it.
    /// </summary>
    public struct ExternalPowerInput : IComponentData
    {
        /// <summary>Total bonus power in MW from all external sources.</summary>
        public int BonusMW;
    }

    /// <summary>
    /// Per-source external power contribution. Aggregated into ExternalPowerInput.
    /// </summary>
    public struct ExternalPowerSource : IComponentData
    {
        /// <summary>Bonus power contribution in MW from this source.</summary>
        public int BonusMW;
    }
}
