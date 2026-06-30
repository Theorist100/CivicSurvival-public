using Game.Areas;
using Game.Buildings;
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
        // ════════════════════════════════════════════════════════════════
        // READ-ONLY LOOKUPS
        // ════════════════════════════════════════════════════════════════

        [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenterLookup;
        [ReadOnly] public ComponentLookup<ElectricityConsumer> ElectricityLookup;
        [ReadOnly] public ComponentLookup<CurrentDistrict> DistrictLookup;
        [ReadOnly] public ComponentLookup<EnvyAffected> EnvyAffectedLookup;
        [ReadOnly] public BufferLookup<HouseholdCitizen> HouseholdCitizenLookup;
        [ReadOnly] public ComponentLookup<Citizen> CitizenLookup;

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

        // Impact pressure (from ImpactPressureSingleton buffer, snapshotted by MHR)
        [ReadOnly] public NativeArray<ImpactDistrictEntry> RecentImpacts;
        public float ImpactDistantFactor;

        // ════════════════════════════════════════════════════════════════
        // TIME
        // ════════════════════════════════════════════════════════════════

        public float DeltaTime;
        public float DeltaHours;
        public float CurrentTime;
        public bool ForceResistanceRecalculate;

        // ════════════════════════════════════════════════════════════════
        // EXECUTE
        // ════════════════════════════════════════════════════════════════

        public void Execute(
            Entity entity,
            ref HouseholdPsyState psy)
        {
            // Reconstruct vanilla household from embedded Index/Version
            Entity household = psy.GetHouseholdEntity();

            // Lookup PropertyRenter on vanilla household to get building
            if (!PropertyRenterLookup.TryGetComponent(household, out var renter))
                return; // Household lost property (homeless) — skip

            Entity building = renter.m_Property;

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
                out psy.Exposure_EnemyInternet,
                out psy.Exposure_EnemyIPSO,
                out psy.Exposure_CounterOps,
                out psy.Exposure_StateMedia);

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
            // 5. COGNITIVE STATE (infection)
            // ════════════════════════════════════════════════════════════

            psy.InfectionLevel = CognitiveCalculator.Calculate(
                psy.InfectionLevel,
                psy.Exposure_EnemyInternet,
                psy.Exposure_EnemyIPSO,
                psy.Exposure_StateMedia,
                psy.Exposure_CounterOps,
                psy.Resistance_Value,
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
                BlackoutVulnMaxBonus);

            // ════════════════════════════════════════════════════════════
            // 5b. IMPACT PRESSURE (from recent rocket/ballistic hits)

            // Same-district hits compound with diminishing returns; distant hits stay max.
            float impactPressure = 0f;
            float distantMax = 0f;
            for (int i = 0; i < RecentImpacts.Length; i++)
            {
                float intensity = RecentImpacts[i].Intensity;
                if (districtIdx == RecentImpacts[i].DistrictIndex)
                    impactPressure += intensity;
                else
                    distantMax = math.max(distantMax, intensity * ImpactDistantFactor);
            }
            float localImpact = 1f - math.exp(-math.max(0f, impactPressure));
            psy.Pressure_Impact = math.saturate(math.max(localImpact, distantMax));
            psy.HasEnvyPressure = psy.Pressure_Envy > 0.001f;
            psy.HasImpactPressure = psy.Pressure_Impact > 0.001f;

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
