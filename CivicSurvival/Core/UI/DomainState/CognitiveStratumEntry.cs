namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One per-stratum opinion lane for the cognitive Opinion Board.
    /// Sourced from the per-stratum read model in <c>CognitiveStatsState</c>
    /// (Poor/Middle/Wealthy counts + avg infection/resistance).
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen
    /// owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct CognitiveStratumEntry
    {
        /// <summary>Social stratum (1 = Poor, 2 = Middle, 3 = Wealthy; mirrors SocialStratum).</summary>
        public int Stratum;
        /// <summary>Households classified into this stratum.</summary>
        public int Count;
        /// <summary>Average infection level across the stratum, 0..1.</summary>
        public float Infection;
        /// <summary>Average resistance across the stratum, 0..1.</summary>
        public float Resistance;

        // ── Phase 13A — measurable psy-war (households currency) ──
        /// <summary>Households the Center allocates to this stratum's coverage = allocation weight ×
        /// the citywide coverage-capacity ceiling.</summary>
        public int AllocatedHouseholds;
        /// <summary>Households actually covered = combined coverage fraction (min of telecom signal and
        /// capacity) × stratum households.</summary>
        public int CoveredHouseholds;
        /// <summary>Estimated households under live enemy reach into this stratum — the aggregate of the
        /// in-flight raids' reach proxy (Intensity × vulnerability × households) targeting the seam.
        /// Fog-gated: only raids whose own intel fog reads REVEALED contribute (mirrors the per-contact
        /// ReachHouseholds gate), so the lanes never leak what the contact cards withhold.</summary>
        public int EnemyReachHouseholds;
        /// <summary>True while at least one in-flight raid is still fogged (not REVEALED) — its reach is
        /// withheld from EnemyReachHouseholds and its seam is unknown, so the flag is raised for ALL
        /// strata. The UI renders an honest "+?" / no-intel verdict instead of a false calm zero.</summary>
        public bool HasFoggedReach;
        /// <summary>Telecom-signal coverage ceiling for this stratum, 0..1 — the fraction of the stratum's
        /// households inside strong telecom signal. The combined coverage is min(signal, capacity), so this
        /// is the point past which extra capacity allocation is wasted (the allocation slider's cap tick).</summary>
        public float SignalCoverageFraction;
    }
}
