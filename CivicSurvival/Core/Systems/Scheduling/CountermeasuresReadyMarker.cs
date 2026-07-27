using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that countermeasures producers have completed their update.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies.
    /// - Producer systems order themselves before this marker
    /// - ScandalSystem (Corruption) reads Heat/CorruptionScore
    /// - ShadowTradeDailySystem intentionally reads previous-frame CountermeasuresCoreFsm
    ///   and must not order after this marker, or the ShadowTrade -> Corruption
    ///   -> Countermeasures chain becomes cyclic.
    /// </summary>
    public partial class CountermeasuresReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
