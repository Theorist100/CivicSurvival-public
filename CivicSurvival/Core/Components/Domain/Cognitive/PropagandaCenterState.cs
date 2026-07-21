using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Derived city-wide Propaganda Center posture. Single-writer
    /// (<c>PropagandaCenterSystem</c>, CIVIC175) — rebuilt each throttled tick from the placed
    /// <c>PropagandaCenterInstallation</c> entities + the vanilla <c>TelecomCoverage</c> cell
    /// map. NOT serialized: recreated on load and rebuilt on the first post-load tick (the
    /// installations it derives from ARE serialized), mirroring the AA live-cache pattern.
    ///
    /// Read cross-domain via <c>SystemAPI.TryGetSingleton</c> by:
    /// - the cognitive coverage projection (broadcast tools only work where the Center reaches);
    /// - the PSYOPS rumor spread (telecom-graph firebreak + quarantine gate);
    /// - the cognitive UI (unlock gate + capacity readout).
    /// </summary>
    public struct PropagandaCenterState : IComponentData
    {
        /// <summary>A Propaganda Center object is placed in the city.</summary>
        public bool IsBuilt;

        /// <summary>
        /// The Center can broadcast (built + powered). For the current placeholder model — the
        /// Patriot SAM prop has no vanilla electricity hookup — this collapses to <see cref="IsBuilt"/>;
        /// when a real <c>CityServiceBuilding</c> model replaces it, add the electricity check here.
        /// This is the unlock condition for the counter-propaganda actions (Telemarathon + heroes).
        /// </summary>
        public bool IsOnline;

        /// <summary>Highest upgrade tier across placed Centers (0 = base). Growth = upgrades, not copies.</summary>
        public int UpgradeTier;

        /// <summary>Total broadcast capacity budget (base + tier increments). The global limiter.</summary>
        public float BroadcastCapacity;

        /// <summary>Capacity currently drawn by active broadcast tools (Telemarathon + hero speaker).</summary>
        public float CapacityAllocated;

        /// <summary>City telecom coverage fraction [0..1] sampled from vanilla TelecomCoverage. Holes = risk.</summary>
        public float TelecomFactor;

        /// <summary>
        /// Effective counter-propaganda broadcast reach [0..1] = f(online, capacity vs allocation,
        /// telecom). Broadcast countermeasures are scaled by this; 0 when the Center is offline.
        /// </summary>
        public float CenterReach;

        /// <summary>
        /// Phase 13A — coverage-capacity ceiling in HOUSEHOLDS (base + tier increments): the absolute
        /// number of households the Center can hold under counter-propaganda coverage. A SEPARATE
        /// quantity from the abstract <see cref="BroadcastCapacity"/> tool-budget above (they never
        /// conflate). Split across strata by the three <c>AllocWeight*</c> below. Derived — mirrored
        /// from config + tier each tick.
        /// </summary>
        public float CoverageCapacityHouseholds;

        /// <summary>
        /// Effective broadcast-coverage allocation weights (Poor/Middle/Wealthy), normalized to sum 1
        /// — the persisted player split (from the primary <c>PropagandaCenterInstallation</c>), or a
        /// population default until the player allocates. Mirrored each tick;
        /// <c>allocatedHouseholds[stratum] = AllocWeight[stratum] * CoverageCapacityHouseholds</c>.
        /// </summary>
        public float AllocWeightPoor;
        public float AllocWeightMiddle;
        public float AllocWeightWealthy;

        /// <summary>Free capacity headroom (BroadcastCapacity − CapacityAllocated, floored at 0).</summary>
        public float CapacityFree => BroadcastCapacity > CapacityAllocated ? BroadcastCapacity - CapacityAllocated : 0f;

        public static readonly PropagandaCenterState Default = default;
    }
}
