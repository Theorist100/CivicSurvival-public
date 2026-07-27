using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker — fires after ShadowTradeDailySystem (ShadowEconomy) settles
    /// the per-day shadow import/export state. Cross-feature consumers order
    /// themselves after this marker without referencing the producer's type
    /// directly (preserves Axiom 5 once files move into Domains/ShadowEconomy/).
    ///
    /// D5 (locked).
    /// </summary>
    public partial class ShadowTradeReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
