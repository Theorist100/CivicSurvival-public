using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker for systems that produce input signals for pressure channels.
    ///
    /// Writers:
    /// - NeighborEnvySystem (NeighborEnvy domain) → EnvyAffected tags → Pressure_Envy
    ///
    /// NOTE: BlackoutStressSystem removed — logic moved to BlackoutCalculator
    /// (called directly from ResolveHouseholdPsyJob).
    ///
    /// Consumer:
    /// - MentalHealthResolverSystem is registered after this marker.
    ///
    /// Pipeline validation: see PressureRegistry (fail-fast orphan detection).
    ///
    /// Purpose: Decouple cross-domain scheduling dependencies.
    /// Domains import Core (this marker), not each other.
    /// </summary>
    public partial class PsyPressureWriterGroup : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
