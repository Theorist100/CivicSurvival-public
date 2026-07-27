namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One live discrete PSYOPS attack contact for the cognitive Opinion Board /
    /// INTEL screen. Sourced from a query over <c>PsyOpsAttack</c>
    /// entities; times are derived from absolute TotalGameHours stamps minus the
    /// current game hour, so the interception window ticks down as the binding
    /// republishes (~500ms panel throttle). Display-only — there is no player
    /// interception trigger yet (that needs Phase-06 counter-operations backend).
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen
    /// owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct CognitivePsyOpsEntry
    {
        /// <summary>Attack carrier (0 = Propaganda, 1 = Rumor, 2 = FakeVideo; mirrors PsyOpsAttackType).</summary>
        public int PsyOpsType;
        /// <summary>Target stratum (1 = Poor, 2 = Middle, 3 = Wealthy; mirrors SocialStratum).</summary>
        public int TargetStratum;
        /// <summary>Lifecycle phase (0 = InFlight, 1 = Landed, 2 = Expired; mirrors PsyOpsAttackPhase).</summary>
        public int Phase;
        /// <summary>Game-hours left until the interception window closes (0 once closed).</summary>
        public float WindowHours;
        /// <summary>Interception window remaining as a fraction of the full window, 0..1 (drives a closing bar).</summary>
        public float WindowFraction;
        /// <summary>Game-hours until the attack reaches its target (0 once landed).</summary>
        public float EtaHours;
        /// <summary>Attack strength at launch, 0..1 (wave-scaled). Bucketed to a coarse step while fogged.</summary>
        public float Intensity;
        /// <summary>True for the raid's dominant contact — the gap-biased main threat to read (only surfaced once <see cref="FogState"/> ≥ Detected).</summary>
        public bool IsDominant;
        /// <summary>
        /// Intel fog tier over this contact: 0 = Hidden (signal only — type/target withheld),
        /// 1 = Detected (type shown, values coarse), 2 = Revealed (exact). Derived in the reader from
        /// the shared Intel state (insider / upgrade level) and proximity to land — never persisted.
        /// </summary>
        public int FogState;

        // ── Phase 13C — legible counter outcome. Two honest signals: LIVE (recomputed under the
        //    current hero posture) for an InFlight contact — the actionable forecast; the FROZEN
        //    land snapshot (PsyOpsAttack.LandedHeroShield / LandedBlunted) for a Landed contact —
        //    the historical verdict a later speaker switch must not rewrite. ──
        /// <summary>
        /// Effective RPS hero cut on this contact's impact term, 0..1 — the exact
        /// <c>× (1 − HeroShield)</c> factor the household resolver applies
        /// (StratumDefense.HeroTypeEffectiveness for the active archetype vs this attack type;
        /// 0 when no hero is active or the type is still fogged). Live posture for InFlight;
        /// the frozen land snapshot for Landed. Shown SEPARATELY from coverage —
        /// never multiplied together (coverage is a defence subtraction, not an attack multiplier).
        /// </summary>
        public float HeroShield;
        /// <summary>1 when the archetype hard-counters this attack type at full strength
        /// (the strong RPS branch — "blunted"); 0 for the weak blanket, no counter, or a fogged type.
        /// Live posture for InFlight; the frozen land snapshot for Landed.</summary>
        public int Blunted;
        /// <summary>Target stratum's telecom coverage fraction, 0..1 — the aggregate defence context
        /// shown alongside the hero cut (0 while the seam is fogged).</summary>
        public float TargetCoverage;
        /// <summary>Estimated households this strike reaches in its target stratum
        /// (≈ Intensity × stratum vulnerability × stratum households). The readout splits it into
        /// shielded (× HeroShield) vs hit (× 1−HeroShield). 0 while the seam is fogged.</summary>
        public int ReachHouseholds;
        /// <summary><c>(int)HeroArchetype</c> of the speaker at the moment of landing, from the
        /// frozen land snapshot ("countered by {0}"); -1 = not landed yet or no hero was active.</summary>
        public int LandedByArchetype;
        /// <summary>Stable per-contact identity (the attack's launch-frozen Seed reinterpreted as int) —
        /// a React key that survives list reorder/expiry, unlike the list index.</summary>
        public int ContactId;
    }
}
