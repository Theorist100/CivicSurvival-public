using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Cognitive warfare domain DTO.
    /// cognitiveDistricts stays as separate ValueBinding with custom IWriter.
    /// </summary>
    public partial struct CognitiveDto : IDomainDto
    {
        // Core state
        public bool CognitiveActive;
        public float InfectionRate;
        public float RecoveryRate;
        public float PenaltyThreshold;
        public int TotalDistricts;
        public int CompromisedDistricts;

        // Hero unit
        public int HeroStatus;
        public int HeroDeployCost;
        public float HeroInfectionReduction;
        public float HeroRecoveryBonus;
        public string HeroActionRequestJson;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanDeployHero), "DeployHeroLockedReasonId")]
        public bool CanDeployHero;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanRecallHero), "RecallHeroLockedReasonId")]
        public bool CanRecallHero;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanSetHeroCounter), "SetHeroCounterLockedReasonId")]
        public bool CanSetHeroCounter;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanSetHeroLecturing), "SetHeroLecturingLockedReasonId")]
        public bool CanSetHeroLecturing;

        // Speaker archetype: 0 = Voice/Gerda, 1 = Arestovych, 2 = Patriot.
        public int HeroArchetype;
        // Arestovych "trust debt" as a 0..1 ratio — accrues while Arestovych is active; surfaced as risk.
        public float ArestovychDebtRatio;
        // True while an Arestovych debt-collapse window is active (instant-cut buff suppressed).
        public bool ArestovychDebtCollapsing;
        // True once the deployed-hero archetype can be hot-swapped again (switch-friction cooldown elapsed).
        public bool HeroArchetypeSwitchReady;

        public int ProtestRisk;
        public string DominantNarrative;
        public float AvgIntegrity;

        // Household stats
        public int TotalHouseholds;
        public float AvgInfection;
        public float AvgResistance;
        public float AvgTrauma;
        public int HouseholdsUnderBlackout;
        public int HouseholdsWithEnvy;
        public int HouseholdsUnderImpact;
        public int HouseholdsInfected;

        // Blackout vulnerability
        public int VulnerableHouseholds;
        public float AvgBlackoutHours;
        public float BlackoutVulnerability;

        // Internet mode
        public int InternetMode;
        public float CommercePenalty;
        public string InternetModeRequestJson;

        // IPSO (enemy propaganda)
        public bool IpsoActive;
        public int IpsoIntensity;
        public int IpsoDistrictCount;
        public int IpsoTotalDistricts;

        // Telemarathon
        public bool TelemarathonActive;
        public int NarrativeMode;
        public float MediaTrust;
        public bool IsInShock;
        public float ShockHoursRemaining;
        public float AudienceFatigue;
        public string TelemarathonModeRequestJson;
        public string TelemarathonActiveRequestJson;

        // Propaganda Center — the buildable defence spine + broadcast-capacity budget.
        // CenterOnline is the unlock condition for the counter-propaganda actions (Telemarathon +
        // heroes); Buckwheat (physical aid) stays available without it. Growth = upgrades, not copies.
        public bool CenterBuilt;
        public bool CenterOnline;
        public int CenterTier;
        public int CenterMaxTier;
        public int CenterCost;
        public int CenterUpgradeCost;
        public float BroadcastCapacity;
        public float BroadcastCapacityFree;
        public float CenterReach;      // 0..1 effective counter-propaganda broadcast reach
        public float TelecomFactor;    // 0..1 city telecom coverage (holes = risk zones)
        public string PropagandaCenterPlacementRequestJson;
        public string PropagandaCenterUpgradeRequestJson;

        // ── Phase 13A — coverage-capacity in households (measurable psy-war) ──
        // Absolute household ceiling the Center holds under counter-propaganda coverage (base + tier),
        // split across the three strata by the normalized allocation weights (player lever; default =
        // by-population until allocated). Read-only readout — the sync SetBroadcastAllocation command
        // writes the split. A SEPARATE quantity from BroadcastCapacity (the abstract tool-budget above).
        public int CoverageCapacityHouseholds;
        public float AllocWeightPoor;
        public float AllocWeightMiddle;
        public float AllocWeightWealthy;

        // Rumor tell: aggregate Food resource-run intensity (0-1). >0 = an active panic-buying run.
        public float RumorFoodRunIntensity;

        // FakeVideo tell: citizens currently home on FAKE sick leave (live FakeSickLeave markers).
        // >0 = an active sick-out wave; drains back to 0 as leaves expire.
        public int SickLeaveActiveCount;

        // ── Phase 13B — intel-gated raid forecast (RNG-free recompute in CognitiveUISystem) ──
        // The next raid's LIKELY carrier + seam, surfaced one step ahead and gated by the shared
        // IntelState fog. Recomputed WITHOUT touching the launcher's save-seeded RNG.
        // ForecastType: -1 = unknown (fogged, OR a coverage tie / no active hero); else PsyOpsAttackType (0/1/2).
        public int ForecastType;
        // ForecastStratum: 0 = Unknown (fogged / nothing classified); else SocialStratum (1/2/3) — the most-likely seam.
        public int ForecastStratum;
        // ForecastFog: intel fidelity tier — 0 Hidden ("raid likely" only), 1 Detected (type), 2 Revealed (type + seam).
        public int ForecastFog;

        // ── PSYOPS after-action report (info-war debriefing; kinetic DebriefingModal's sibling) ──
        // ShowPsyDebrief gates the modal; the rest is a FROZEN event-time snapshot. Raid facts are
        // captured the moment a wave's raids finish landing; the report itself (and the damage
        // delta) waits until the wave's landed window closes — a landed raid presses for its whole
        // window, so only the close-time read shows what the strike did. (CognitiveUISystem holds
        // the live→snapshot copy, mirroring the kinetic m_LastDebriefing* pattern.) Not persisted.
        public bool ShowPsyDebrief;
        public int PsyDebriefWave;          // wave number the raids belonged to
        public int PsyDebriefRaidCount;     // raids that landed this wave
        public int PsyDebriefPropaganda;    // per-carrier landed counts
        public int PsyDebriefFakeVideo;
        public int PsyDebriefRumor;
        public int PsyDebriefBlunted;       // how many the deployed speaker blunted
        public int PsyDebriefStrataMask;    // hit strata bitmask: Poor=1, Middle=2, Wealthy=4
        public float PsyDebriefPeakIntensity; // strongest raid intensity 0..1
        public float PsyDebriefMediaTrust;  // media trust snapshot at report time 0..1
        public int PsyDebriefHouseholdsImpact; // households under active psy-impact at report time
        public bool PsyDebriefFoodRunActive;   // a rumor-driven panic-buying run was live
        public int PsyDebriefInfectedDelta;    // households newly infected over the wave's landed window

        // ── PSYOPS inbound alert (the raid is in the air and can still be answered) ──
        // The phase badge alone said "PSY ATTACK" and nothing else: a player who has never met the
        // info-war does not know what is coming, what it does, or that the window closes when it
        // lands. This is the one interrupt that carries the counter-play, and it EXPIRES — the modal
        // is only honest while the raid is still in flight. Fires once per wave, not per carrier.
        // Everything here respects the SAME intel fog as the contact telegraph: an unread carrier
        // reads as unknown rather than a lie.
        public bool ShowPsyInbound;
        public int PsyInboundWave;
        public int PsyInboundCount;        // carriers inbound this wave
        public int PsyInboundType;         // PsyOpsAttackType, or -1 while the carrier is fogged
        public int PsyInboundStratum;      // SocialStratum, or 0 (Unknown) while the seam is fogged
        public float PsyInboundEtaHours;   // game hours until it lands (0 = landing now)
        public bool PsyInboundCounterReady; // a deployed speaker already counters this carrier

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
