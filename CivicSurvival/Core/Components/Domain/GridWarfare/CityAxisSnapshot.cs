using CivicSurvival.Core.Components.CrossDomain;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// A point-in-time read of the raw city metrics that feed the three stability axes —
    /// the SAME inputs <c>Domains.GridWarfare.Systems.CityStabilitySystem</c> gathers from
    /// ECS before delegating to <see cref="CivicSurvival.Core.Logic.CityAxisFormulas"/>. It
    /// carries city <em>shape</em> (blackout coverage, destroyed buildings, fires, power
    /// deficit, grid stress, happiness penalty), not the derived axes themselves.
    ///
    /// This is the seam that makes the mirror enemy "the same form as a city". The enemy's
    /// starting axes are <em>derived</em> from a snapshot via
    /// <see cref="EnemyState.FromSnapshot"/> instead of being free cap-numbers: PvE builds a
    /// synthetic snapshot (<see cref="SyntheticEnemy"/>) and the future PvP raid (Wave 4) fills
    /// the SAME struct from another player's serialized city JSON — so the enemy shape is
    /// identical and only the data source changes.
    ///
    /// It is a pure initialization form (a seed), NOT live combat state. Once
    /// <see cref="EnemyState.FromSnapshot"/> has produced the axes, the axes are the stored,
    /// mutable battle state (lowered by <see cref="EnemyState.ReduceAxis"/>, raised by regen);
    /// the snapshot is not re-read per tick and is not persisted by the enemy save block — the
    /// axes already are. The PvP raid singleton (Wave 4) decides whether <em>it</em> persists a
    /// snapshot for its own offline-defence recompute; the PvE enemy does not need to.
    ///
    /// Blittable over <c>int</c> / <c>float</c> / <see cref="GridStatusType"/> (a byte enum),
    /// no ECS or config dependency. The denominators (district / building caps, happiness
    /// ceiling) are NOT stored here — those are remote-config balance values the mapper reads
    /// from <c>CityStabilityConfig</c>, exactly as the runtime city does. A snapshot is purely
    /// the city's measured counts; the cap a count is rationed against is tuning, not shape.
    /// </summary>
    public struct CityAxisSnapshot
    {
        // --- Physical axis inputs (CityAxisFormulas.PhysicalInstability) ---

        /// <summary>Districts currently under a blackout penalty (numerator of blackout coverage).</summary>
        public int AffectedDistricts;

        /// <summary>Buildings destroyed (numerator of the destroyed-buildings ratio).</summary>
        public int BuildingsDestroyed;

        /// <summary>Buildings currently on fire (numerator of the fires ratio).</summary>
        public int BuildingsOnFire;

        // --- Digital axis inputs (CityAxisFormulas.DigitalInstability) ---

        /// <summary>
        /// Signed power balance (production − load); only a negative value with positive
        /// <see cref="Consumption"/> contributes a deficit term, matching the runtime city read.
        /// </summary>
        public int PowerBalance;

        /// <summary>Power consumption (deficit-ratio denominator; a zero consumption yields no deficit term).</summary>
        public int PowerConsumption;

        /// <summary>Grid stress status (Normal/Warning/Critical/Surplus → 0 / 0.5 / 1.0 / 0 stress factor).</summary>
        public GridStatusType GridStatus;

        // --- Social axis input (CityAxisFormulas.SocialInstability) ---

        /// <summary>Maximum happiness penalty (numerator of the social-instability ratio).</summary>
        public float MaxHappinessPenalty;

        /// <summary>
        /// The synthetic "default enemy" snapshot for PvE: a fully healthy city (no blackout,
        /// no destruction, no fires, balanced grid, no happiness penalty). Fed through
        /// <see cref="EnemyState.FromSnapshot"/> this yields all three axes at full health (cap),
        /// reproducing the prior <c>EnemyState.Default</c> behaviour — but now via the snapshot
        /// path, so PvE and a future PvP raid share one initialization route.
        /// </summary>
        public static CityAxisSnapshot SyntheticEnemy => new()
        {
            AffectedDistricts = 0,
            BuildingsDestroyed = 0,
            BuildingsOnFire = 0,
            PowerBalance = 0,
            PowerConsumption = 0,
            GridStatus = GridStatusType.Normal,
            MaxHappinessPenalty = 0f
        };
    }
}
