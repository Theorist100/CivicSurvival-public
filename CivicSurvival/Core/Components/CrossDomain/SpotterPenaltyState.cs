using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Published penalty facts from the Spotters domain.
    /// Consumed by AirDefense systems to adjust intercept chance.
    ///
    /// Writer: SpotterAggregateSystem
    /// Readers: AirDefenseOrchestrator, BallisticDefenseSystem, AirDefenseUISystem
    ///
    /// Split from SpotterStatsSingleton — external domains need penalty facts only,
    /// not the full spotter stats (counts, costs, visits).
    /// </summary>
    public struct SpotterPenaltyState : IComponentData
    {
        /// <summary>Raw penalty before Counter-OSINT reduction (0–1).</summary>
        public float RawPenalty;

        /// <summary>Final penalty applied to intercept chance after Counter-OSINT (0–1).</summary>
        public float GlobalPenalty;

        /// <summary>Is Counter-OSINT operation currently active?</summary>
        public bool IsCounterOSINTActive;

        public static SpotterPenaltyState Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
