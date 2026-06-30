using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals DamageStatsUpdateSystem (DamageAccounting cross-domain feature)
    /// has updated.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - DamageAccountingFeature RegisterAfter&lt;DamageStatsReadyMarker, DamageStatsUpdateSystem&gt;
    ///   so the marker runs after the producer.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, DamageStatsReadyMarker&gt;
    ///   without importing CivicSurvival.Core.Features.CrossDomain.DamageAccounting.
    ///
    /// Consumers: CityStabilitySystem (GridWarfare).
    /// </summary>
    public partial class DamageStatsReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
