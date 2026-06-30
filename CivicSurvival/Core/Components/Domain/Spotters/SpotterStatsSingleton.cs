using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Spotters
{
    /// <summary>
    /// Global spotter statistics as ECS singleton.
    /// UI-facing counts, costs, and action counters.
    ///
    /// Access: SystemAPI.GetSingleton&lt;SpotterStatsSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;SpotterStatsSingleton&gt;()
    ///
    /// Writer: SpotterAggregateSystem
    /// Readers: AirDefenseUISystem, MilestoneTutorialSystem
    ///
    /// Penalty facts (GlobalPenalty, RawPenalty, IsCounterOSINTActive) are in
    /// SpotterPenaltyState (CrossDomain) — read by AirDefense operational systems.
    ///
    /// Implements ISerializable with no-op bodies to opt out of CS2's default
    /// positional serializer. Stats are derived from active spotter entities.
    ///
    /// For per-district internet status, query InternetDisabledBuffer on
    /// SpotterCountermeasuresState singleton entity directly.
    /// </summary>
    public struct SpotterStatsSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>Total number of spotter entities (active + silenced)</summary>
        public int TotalCount;

        /// <summary>Number of active spotters (not silenced, not in internet-disabled districts)</summary>
        public int ActiveCount;

        /// <summary>Number of active spotters that SBU/Evacuation commands can target</summary>
        public int ActionableCount;

        /// <summary>Current SBU visit cost (progressive pricing based on TotalSBUVisits)</summary>
        public int SBUCost;

        /// <summary>Total SBU visits performed this session</summary>
        public int TotalSBUVisits;

        /// <summary>Total evacuations performed this session</summary>
        public int TotalEvacuations;

        /// <summary>Evacuation cost (from config, cached for UI)</summary>
        public int EvacuationCost;

        /// <summary>Counter-OSINT daily cost (from config, cached for UI)</summary>
        public int CounterOSINTDailyCost;

        public static SpotterStatsSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        // IEmptySerializable marker: stats recalculated every frame by
        // SpotterAggregateSystem — no persisted payload.

        public void SetDefaults() { this = Default; }
    }
}
