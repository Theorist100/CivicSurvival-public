using Game.Areas;
using Game.Buildings;
using Game.Economy;
using Game.Simulation;
using CivicSurvival.Core.Features.Wellbeing;
using Game.Citizens;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using Unity.Mathematics;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.Domain.NeighborEnvy;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Cognitive.Core.Jobs
{
    /// <summary>
    /// CONSOLIDATED JOB: Single writer for all HouseholdPsyState fields.
    /// Calls all 6 calculators in sequence for each household.
    ///
    /// Logic Composition pattern:
    /// - BlackoutCalculator: power status → pressure, hours
    /// - EnvyCalculator: neighbor power → pressure
    /// - ExposureCalculator: district/hero/telemarathon → exposure
    /// - ResistanceCalculator: education → resistance (throttled)
    /// - CognitiveCalculator: exposure → infection
    /// - TraumaCalculator: pressure → trauma, inertia
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct ResolveHouseholdPsyJob : IJobEntity
    {
        // Vanilla TelecomCoverage grid geometry: 128×128 cells, signal stored as a byte.
        // Internal: MentalHealthResolverSystem gates the array on this exact size, so the
        // bound assumed by CellIndex below is guaranteed at the single admission point.
        internal const int TELECOM_GRID_SIZE = 128;
        private const float SIGNAL_BYTE_MAX = 255f;

        // ════════════════════════════════════════════════════════════════
        // READ-ONLY LOOKUPS
        // ════════════════════════════════════════════════════════════════

        [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenterLookup;
        [ReadOnly] public ComponentLookup<ElectricityConsumer> ElectricityLookup;
        [ReadOnly] public ComponentLookup<CurrentDistrict> DistrictLookup;
        [ReadOnly] public ComponentLookup<EnvyAffected> EnvyAffectedLookup;
        [ReadOnly] public BufferLookup<HouseholdCitizen> HouseholdCitizenLookup;
        [ReadOnly] public ComponentLookup<Citizen> CitizenLookup;

        // Social stratum classification (read-only vanilla wealth)
        [ReadOnly] public ComponentLookup<Household> HouseholdLookup;
        [ReadOnly] public BufferLookup<Resources> ResourcesLookup;

        // Vanilla telecom coverage read (building position → 128² signal grid cell).
        // Read-only vanilla map handed in by MHR (GetMap(readOnly:true) + AddReader — no .Complete()).
        [ReadOnly] public ComponentLookup<Game.Objects.Transform> BuildingTransformLookup;
        [ReadOnly] public NativeArray<TelecomCoverage> TelecomSignal;
        public float TelecomThreshold;
        public bool HasTelecomData;

        // Internet disabled districts (from SpotterCountermeasuresState)
        [ReadOnly] public NativeParallelHashSet<int> InternetDisabledDistricts;
        public bool HasInternetData;

        // IPSO per-district exposure (from IPSOState singleton buffer)
        [ReadOnly] public NativeHashMap<int, float> IPSODistrictExposure;
        public bool HasIpsoData;

        // T4-7 FIX: Global Internet Blackout mode (blocks ALL internet exposure)
        public bool IsGlobalBlackout;

        // ════════════════════════════════════════════════════════════════
        // SINGLETON VALUES (extracted in system OnUpdate)
        // ════════════════════════════════════════════════════════════════

        public BackupPolicy BackupPolicy;
        public HeroStatus HeroStatus;
        // Active speaker archetype (hero-by-type counter + per-stratum tilt).
        public HeroArchetype Archetype;

        // Three-layer battery coverage (per-district)
        [ReadOnly] public NativeHashMap<int, DistrictBatteryCoverage> DistrictCoverageMap;
        public float MitigationWeightHospital;
        public float MitigationWeightSchool;
        public float MitigationWeightPrivate;
        public float MitigationMin;

        // Telemarathon state
        public bool TelemarathonActive;
        public bool TelemarathonInShock;
        public float TelemarathonTrust;
        public float TelemarathonEffectiveness;
        public float TelemarathonModeBonus;
        public float AlarmistStressRate;

        // ════════════════════════════════════════════════════════════════
        // CONFIG VALUES (from BalanceConfig.Cognitive)
        // ════════════════════════════════════════════════════════════════

        // Cognitive calculator
        public float EnemyInternetWeight;
        public float EnemyIpsoWeight;
        public float CounterOpsMultiplier;
        public float SkepticismFactor;
        public float InfectionRate;
        public float RecoveryRate;
        public float BlackoutVulnThreshold;
        public float BlackoutVulnMaxHours;
        public float BlackoutVulnMaxBonus;

        // Trauma calculator
        public float EnvyStress;
        public float TraumaGainRate;
        public float TraumaDecayRate;
        public float InertiaGainRate;
        public float InertiaDecayRate;

        // Physical impact events (from ImpactPressureSingleton buffer, snapshotted by MHR,
        // delivered once per slot). Stratum-agnostic — they feed the trauma vector.
        [ReadOnly] public NativeArray<ImpactDistrictEntry> RecentImpacts;
        public float ImpactDistantFactor;
        // Live landed PSYOPS attacks (rebuilt from the PsyOpsAttack entities every fire) — standing
        // per-stratum pressure for the whole landed window, integrated over DeltaHours below.
        // Separate from RecentImpacts: an event is delivered once, a state presses while it lives.
        [ReadOnly] public NativeArray<ImpactDistrictEntry> LiveRaidPressures;
        // Weight of the discrete-raid infection channel (a landed PSYOPS contact matched to this
        // household's stratum). The raid channel is separate from the ambient field: the broadcast
        // defence does not blunt it — the hero's by-type shield and aid do.
        public float ImpactInfectionWeight;
        // Infection per hour added per unit of matched raid pressure (contract: perHour) —
        // integrated over DeltaHours like the ambient field, so the landed window's height ×
        // duration is the dose profile.
        public float RaidInfectionRate;

        // ── Hero-by-type + tool-by-stratum effectiveness ──
        // Hero counter effectiveness vs the discrete attack TYPE (RPS). HeroCounterStrong is the
        // EFFECTIVE strong value the system passes — it equals HeroCounterFloor while an Arestovych
        // trust-debt collapse is suppressing the counter (computed in MentalHealthResolverSystem).
        public float HeroCounterStrong;
        public float HeroCounterFloor;
        // Patriot raises household resistance broadly (its sustain identity) while active.
        public float PatriotResistanceBonus;
        // Telemarathon (state media) per-stratum effectiveness, applied in ExposureCalculator.
        public float TelemarathonStratumStrong;
        public float TelemarathonStratumFloor;
        // Patriot per-stratum tilt (signed) — wealthy backfire (capital flight) + presses the poor.
        public float PatriotWealthyBackfire;
        public float PatriotPoorPress;
        // Districts under an active Buckwheat food-aid effect + its per-stratum effectiveness:
        // food calms the poor (strong defence), aid to the wealthy backfires (added pressure).
        [ReadOnly] public NativeParallelHashSet<int> BuckwheatAidedDistricts;
        public bool HasBuckwheatData;
        public float BuckwheatStratumPoor;
        public float BuckwheatStratumFloor;
        public float BuckwheatWealthyBackfire;

        // ════════════════════════════════════════════════════════════════
        // TIME
        // ════════════════════════════════════════════════════════════════

        public float DeltaTime;
        public float DeltaHours;
        public float CurrentTime;
        public bool ForceResistanceRecalculate;

        // Vanilla wealth thresholds (CitizenHappinessParameterData.m_WealthyMoneyAmount, int4 x/y/z/w).
        // HasWealthThresholds = false when the prefab singleton isn't loaded yet (early frames) →
        // stratum left untouched (stays Unknown) rather than misclassified.
        public int4 WealthThresholds;
        public bool HasWealthThresholds;

        // ════════════════════════════════════════════════════════════════
        // EXECUTE
        // ════════════════════════════════════════════════════════════════

        public void Execute(
            Entity entity,
            in PsyHouseholdLink link,
            ref HouseholdPsyState psy)
        {
            // Reconstruct vanilla household from the sidecar's identity link
            Entity household = link.GetHouseholdEntity();

            // Lookup PropertyRenter on vanilla household to get building
            if (!PropertyRenterLookup.TryGetComponent(household, out var renter))
            {
                // Household lost property (homeless): stratum is no longer resolvable.
                // Reset to Unknown so downstream consumers never read a stale class
                // carried over from a previous housed state — this base classification is the
                // foundation later attack/defense phases read CachedStratum directly.
                psy.CachedStratum = SocialStratum.Unknown;
                // Homeless → no building → outside all telecom coverage (broadcast defence unreachable).
                psy.InCoverage = false;
                return;
            }

            Entity building = renter.m_Property;

            // ════════════════════════════════════════════════════════════
            // TELECOM COVERAGE: sample the vanilla 128² signal grid at the building's cell.
            // "In coverage" = the pre-computed signal (radius + terrain shadow + tower overlap, folded
            // by vanilla) is at/above the threshold. Each entity writes its OWN InCoverage → race-free
            // in the parallel resolver; derived → self-heals on load. Homeless handled above.
            // ════════════════════════════════════════════════════════════
            // Transform read via the indexer (the job declares BuildingTransformLookup [ReadOnly] and is
            // scheduled on Dependency, so the render/transform writers are fenced by the job graph — the
            // sanctioned in-job form, distinct from a main-thread ticket).
            psy.InCoverage = false;
            if (HasTelecomData && BuildingTransformLookup.HasComponent(building))
            {
                float3 buildingPos = BuildingTransformLookup[building].m_Position;
                int2 cell = math.clamp(
                    CellMapSystem<TelecomCoverage>.GetCell(
                        buildingPos, CellMapSystem<TelecomCoverage>.kMapSize, TELECOM_GRID_SIZE),
                    0, TELECOM_GRID_SIZE - 1);
                int cellIdx = cell.x + TELECOM_GRID_SIZE * cell.y;
                float signal = TelecomSignal[cellIdx].m_SignalStrength / SIGNAL_BYTE_MAX;
                psy.InCoverage = signal >= TelecomThreshold;
            }

            // ════════════════════════════════════════════════════════════
            // 0. SOCIAL STRATUM (read-only vanilla wealth classification)
            // Resolved FIRST so the stratum is available to ExposureCalculator
            // (tool stratum-effectiveness + hero per-stratum tilt) and the impact loop in the
            // same pass. If thresholds/lookups are not ready the cached value is left untouched
            // (stays Unknown after load) — stratum-keyed effects simply do not match until it resolves.
            // ════════════════════════════════════════════════════════════

            if (HasWealthThresholds
                && HouseholdLookup.TryGetComponent(household, out var householdData)
                && ResourcesLookup.TryGetBuffer(household, out var resourceBuffer))
            {
                int totalWealth = EconomyUtils.GetHouseholdTotalWealth(householdData, resourceBuffer);
                psy.CachedStratum = SocialStratumClassifier.Classify(totalWealth, WealthThresholds);
            }

            // ════════════════════════════════════════════════════════════
            // 1. BLACKOUT PRESSURE (three-layer coverage mitigation)
            // ════════════════════════════════════════════════════════════

            bool hasPower = GetBuildingPower(building);
            int districtIdx = GetDistrictIndex(building);

            // Get per-district coverage for three-layer mitigation
            float hospitalCov = 0f;
            float schoolCov = 0f;
            float privateCov = 0f;
            if (districtIdx >= 0 && DistrictCoverageMap.TryGetValue(districtIdx, out var cov))
            {
                hospitalCov = cov.HospitalCoverage;
                schoolCov = cov.SchoolCoverage;
                privateCov = cov.PrivateCoverage;
            }

            BlackoutCalculator.Calculate(
                hasPower,
                BackupPolicy,
                DeltaHours,
                hospitalCov,
                schoolCov,
                privateCov,
                MitigationWeightHospital,
                MitigationWeightSchool,
                MitigationWeightPrivate,
                MitigationMin,
                ref psy.BlackoutHours,
                out psy.Pressure_Blackout);

            // ════════════════════════════════════════════════════════════
            // 2. ENVY PRESSURE
            // ════════════════════════════════════════════════════════════

            bool isEnvyAffected = EnvyAffectedLookup.HasComponent(building)
                && EnvyAffectedLookup.IsComponentEnabled(building);
            psy.Pressure_Envy = EnvyCalculator.Calculate(hasPower, isEnvyAffected, EnvyStress);

            // ════════════════════════════════════════════════════════════
            // 3. COGNITIVE EXPOSURE
            // ════════════════════════════════════════════════════════════

            ExposureCalculator.Calculate(
                districtIdx,
                InternetDisabledDistricts,
                HasInternetData,
                IPSODistrictExposure,
                HasIpsoData,
                IsGlobalBlackout,
                HeroStatus,
                TelemarathonActive,
                TelemarathonInShock,
                TelemarathonTrust,
                TelemarathonEffectiveness,
                TelemarathonModeBonus,
                psy.CachedStratum,
                Archetype,
                TelemarathonStratumStrong,
                TelemarathonStratumFloor,
                PatriotWealthyBackfire,
                PatriotPoorPress,
                psy.InCoverage,
                out psy.Exposure_EnemyInternet,
                out psy.Exposure_EnemyIPSO,
                out psy.Exposure_CounterOps,
                out psy.Exposure_StateMedia,
                out float heroDefenseTilt);

            // ════════════════════════════════════════════════════════════
            // 4. RESISTANCE (throttled ~30s)
            // ════════════════════════════════════════════════════════════

            if (ForceResistanceRecalculate || ResistanceCalculator.ShouldRecalculate(CurrentTime, psy.Resistance_LastUpdateTime))
            {
                if (TryGetAverageEducation(household, out float avgEducation))
                {
                    psy.Resistance_Value = ResistanceCalculator.FromEducation(avgEducation);
                    if (!ForceResistanceRecalculate)
                        psy.Resistance_LastUpdateTime = CurrentTime;
                }
            }

            // ════════════════════════════════════════════════════════════
            // 5b. IMPACT: physical hit events → trauma; live landed PSYOPS → infection pressure.
            // Same-district physical hits compound with diminishing returns; distant hits stay
            // scaled. A landed raid (LiveRaidPressures) only reaches its own stratum and feeds
            // cognitive infection (via CognitiveCalculator below) — it does NOT raise the trauma
            // vector. Both are per-household × per-entry loops over tiny arrays — no new
            // O(households) pass (INDEX Invariant 2 / Axiom perf).
            // ════════════════════════════════════════════════════════════
            float impactPressure = 0f;   // physical, same-district → trauma
            float distantMax = 0f;       // physical, distant → trauma
            float psyImpactSum = 0f;     // PSYOPS, matched stratum → infection pressure
            bool heroActive = HeroStatus != HeroStatus.Inactive;
            for (int i = 0; i < RecentImpacts.Length; i++)
            {
                // Physical (stratum-agnostic) impact events → trauma vector. Psy raids do not ride
                // this buffer any more (they are standing pressure, not events — LiveRaidPressures).
                var e = RecentImpacts[i];
                if (districtIdx == e.DistrictIndex)
                    impactPressure += e.Intensity;
                else
                    distantMax = math.max(distantMax, e.Intensity * ImpactDistantFactor);
            }
            for (int i = 0; i < LiveRaidPressures.Length; i++)
            {
                var raid = LiveRaidPressures[i];
                // A landed raid presses only its target stratum.
                // INVARIANT: raid delivery assumes recipients' strata are already resolved. CachedStratum
                // is resolved above (step 0) in this same pass, so a match only misses while a household
                // is still Unknown — i.e. before wealth thresholds have loaded (very early boot) or in a
                // brand-new household's first slot cycle. Raids are standing pressure rebuilt every fire,
                // so an unmatched household simply picks the pressure up on its next resolved cycle.
                if (raid.TargetStratum != psy.CachedStratum)
                    continue;
                // Hero-by-TYPE (rock-paper-scissors): the active archetype strongly
                // dampens its counter-type, weakly the others. HeroCounterStrong is already the
                // debt-suppressed effective value (Arestovych collapse → equals the floor).
                float heroEff = StratumDefense.HeroTypeEffectiveness(
                    Archetype, raid.AttackType, heroActive, HeroCounterStrong, HeroCounterFloor);
                psyImpactSum += raid.Intensity * (1f - heroEff);
            }
            float localImpact = 1f - math.exp(-math.max(0f, impactPressure));
            psy.Pressure_Impact = math.saturate(math.max(localImpact, distantMax));
            psy.HasEnvyPressure = psy.Pressure_Envy > 0.001f;
            psy.HasImpactPressure = psy.Pressure_Impact > 0.001f;

            // ════════════════════════════════════════════════════════════
            // 5. COGNITIVE STATE (infection) — reactive field + discrete PSYOPS impact
            // ════════════════════════════════════════════════════════════

            // Patriot raises household resistance broadly while active (its sustain
            // identity — best vs the slow Propaganda grind). Computed locally; never mutates the
            // persisted cached resistance.
            float effectiveResistance = psy.Resistance_Value
                + (heroActive && Archetype == HeroArchetype.Patriot ? PatriotResistanceBonus : 0f);

            // Buckwheat per-stratum effect on infection for a household in a district under
            // active food aid — folded into the SAME signed defence-tilt channel as the hero tilt
            // (additive, not a second psy-state writer). Poor → strong defence; wealthy → backfire
            // (negative tilt → defence drops → added infection pressure, like Patriot→wealthy).
            float buckwheatTilt = 0f;
            if (HasBuckwheatData && BuckwheatAidedDistricts.Contains(districtIdx))
                buckwheatTilt = StratumDefense.BuckwheatStratumDefense(
                    psy.CachedStratum, BuckwheatStratumPoor, BuckwheatStratumFloor, BuckwheatWealthyBackfire);
            float defenseTilt = heroDefenseTilt + buckwheatTilt;

            psy.InfectionLevel = CognitiveCalculator.Calculate(
                psy.InfectionLevel,
                psy.Exposure_EnemyInternet,
                psy.Exposure_EnemyIPSO,
                psy.Exposure_StateMedia,
                psy.Exposure_CounterOps,
                effectiveResistance,
                psy.BlackoutHours,
                DeltaHours,
                EnemyInternetWeight,
                EnemyIpsoWeight,
                CounterOpsMultiplier,
                SkepticismFactor,
                InfectionRate,
                RecoveryRate,
                BlackoutVulnThreshold,
                BlackoutVulnMaxHours,
                BlackoutVulnMaxBonus,
                psyImpactSum,
                ImpactInfectionWeight,
                RaidInfectionRate,
                defenseTilt);

            // 6. TRAUMA & RECOVERY INERTIA
            // ════════════════════════════════════════════════════════════

            TraumaCalculator.Calculate(
                psy.Pressure_Blackout,
                psy.Pressure_Envy,
                psy.Pressure_Impact,
                AlarmistStressRate,
                DeltaTime,
                DeltaHours,
                TraumaGainRate,
                TraumaDecayRate,
                InertiaGainRate,
                InertiaDecayRate,
                ref psy.Trauma,
                ref psy.RecoveryInertia);

            // NOTE: Transient reset NOT here — transients are inputs for WellbeingResolverSystem.
            // Reset happens at the END of frame in PsyTransientResetSystem.
        }

        // ════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════

        private bool GetBuildingPower(Entity building)
        {
            if (!ElectricityLookup.TryGetComponent(building, out var consumer))
                return true; // No consumer = doesn't need power

            // FIX W4-M3: Inlined — hasConsumer always true here (TryGetComponent succeeded)
            return consumer.m_WantedConsumption == 0 || consumer.m_FulfilledConsumption > 0;
        }

        private int GetDistrictIndex(Entity building)
        {
            if (DistrictLookup.TryGetComponent(building, out var district))
                return district.m_District.Index;
            return DistrictUtils.UNZONED_AREA_INDEX;
        }

        private bool TryGetAverageEducation(Entity household, out float avgEducation)
        {
            if (!HouseholdCitizenLookup.TryGetBuffer(household, out var citizens))
            {
                avgEducation = 0f;
                return false;
            }

            float totalEducation = 0f;
            int count = 0;

            for (int i = 0; i < citizens.Length; i++)
            {
                if (CitizenLookup.TryGetComponent(citizens[i].m_Citizen, out var citizen))
                {
                    totalEducation += citizen.GetEducationLevel();
                    count++;
                }
            }

            avgEducation = count > 0 ? totalEducation / count : 0f;
            return count > 0;
        }
    }

    /// <summary>
    /// Job to reset transient fields at END of frame.
    /// Must run AFTER WellbeingResolverSystem has read the values.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct ResetHouseholdPsyTransientsJob : IJobEntity
    {
        public void Execute(ref HouseholdPsyState psy)
        {
            psy.ResetTransients();
        }
    }
}
