using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Game.Buildings;
using Game.Areas;
using Game.Prefabs;
using Game.Citizens;
using Game.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Blackout.Systems
{
    /// <summary>
    /// Burst-compiled parallel job for enforcing blackouts.
    /// Uses IJobEntity for automatic query generation and ScheduleParallel support.
    /// BlackoutState component with IEnableableComponent allows enable/disable without structural changes.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct BlackoutJob : IJobEntity
    {
        // Blackout configuration (ReadOnly for parallel safety)
        [ReadOnly] public NativeHashMap<int, byte> CategoryBlackouts;
        [ReadOnly] public NativeHashMap<int, int> DistrictSchedules;
        [ReadOnly] public NativeHashMap<int, byte> VIPDistricts;
        [ReadOnly] public NativeHashMap<int, byte> VIPBypass;
        [ReadOnly] public float GameHour;

        // Building category lookups (ReadOnly for parallel safety)
        [ReadOnly] public ComponentLookup<ResidentialProperty> ResidentialLookup;
        [ReadOnly] public ComponentLookup<CommercialProperty> CommercialLookup;
        [ReadOnly] public ComponentLookup<IndustrialProperty> IndustrialLookup;
        [ReadOnly] public ComponentLookup<OfficeProperty> OfficeLookup;

        // Backup power: building → live backup mod-entity link map (replaces the BackupPowerRef
        // component that used to hang on vanilla buildings). Read-only snapshot of the read slot.
        [ReadOnly] public NativeHashMap<long, Entity> BackupPowerLinks;
        [ReadOnly] public ComponentLookup<BackupPower> BackupPowerLookup;
        [ReadOnly] public bool BackupPowerEnabled;
        [ReadOnly] public BackupPolicy Policy;

        // VIP Bypass: Wealthy household protection
        [ReadOnly] public BufferLookup<Renter> RenterLookup;
        [ReadOnly] public ComponentLookup<Household> HouseholdLookup;
        [ReadOnly] public BufferLookup<Game.Economy.Resources> ResourcesLookup;
        [ReadOnly] public int WealthyThreshold; // m_WealthyMoneyAmount.w - threshold for "Wealthy"

        // City-wide schedule fallback (used when district has no per-district override)
        [ReadOnly] public int CityScheduleId;

        // Critical infrastructure protection flag (IsCritical set at setup time on BlackoutState)
        [ReadOnly] public bool ProtectCriticalInfra;

        /// <summary>
        /// Execute blackout logic for a single building entity.
        /// Called in parallel for maximum performance.
        /// </summary>
        void Execute(
            Entity entity,
            ref ElectricityConsumer consumer,
            in CurrentDistrict district,
            ref BlackoutState state,
            EnabledRefRW<BlackoutState> enabled)
        {
            // BL-2: Unzoned / No-District buildings (m_District == Entity.Null) are a
            // first-class controllable bucket. They map to NO_DISTRICT_INDEX — the same
            // logical id the UI/state layer keys Unzoned blackouts under — and run the
            // identical ShouldBlackout path. Previously this early-returned, so an
            // Unzoned blackout never cut power even though UI/penalties/events fired.
            int districtIndex = district.m_District == Entity.Null
                ? Engine.Districts.NO_DISTRICT_INDEX
                : district.m_District.Index;

            // If critical infrastructure and protection enabled, clear blackout and skip
            // IsCritical is maintained by BlackoutStateSetupSystem.
            if (state.IsCritical && ProtectCriticalInfra)
            {
                state.ServedByBackup = false;
                // Clear stuck blackout state if building was in blackout when protection was toggled ON
                RestoreConsumptionIfBlackout(enabled);
                return;
            }

            // Determine building category (1-5: Res/Com/Ind/Office/Services)
            int categoryId = GetBuildingCategory(entity);

            // Check if this building should be in blackout
            bool shouldBlackout = ShouldBlackout(districtIndex, categoryId, entity);

            if (shouldBlackout)
            {
                // Check if building has backup power available (policy-aware)
                if (BackupPowerEnabled && HasBackupPowerAvailable(entity, state.HasBatteryPriority))
                {
                    state.ServedByBackup = true;
                    // Clear stuck blackout state if building gained backup power mid-blackout
                    RestoreConsumptionIfBlackout(enabled);
                    return;
                }

                state.ServedByBackup = false;
                if (!enabled.ValueRO)
                {
                    // START BLACKOUT: Zero FulfilledConsumption — WantedConsumption stays intact
                    // This allows vanilla efficiency system to trigger (Fulfilled < Wanted)
                    consumer.m_FulfilledConsumption = 0;
                    enabled.ValueRW = true;
                }
                else
                {
                    // BUG-BLK-004 FIX: Only write if value changed (avoid redundant ECS change tracking)
                    // Defensive write - if vanilla ElectricityGraph tries to restore power, we re-zero it
                    if (consumer.m_FulfilledConsumption != 0)
                        consumer.m_FulfilledConsumption = 0;
                }
            }
            else if (enabled.ValueRO)
            {
                state.ServedByBackup = false;
                // END BLACKOUT: clear only our blackout flag. Vanilla ElectricityGraph owns
                // the next fulfilled-consumption value; pre-filling Wanted here creates a
                // one-frame false "has power" state if the wider grid is still short.
                RestoreConsumptionIfBlackout(enabled);
            }
            else
            {
                state.ServedByBackup = false;
            }
        }

        private static void RestoreConsumptionIfBlackout(EnabledRefRW<BlackoutState> enabled)
        {
            if (!enabled.ValueRO)
                return;

            enabled.ValueRW = false;
        }

        private int GetBuildingCategory(Entity entity)
        {
            if (ResidentialLookup.HasComponent(entity)) return (int)BuildingCategory.Residential;
            if (CommercialLookup.HasComponent(entity)) return (int)BuildingCategory.Commercial;
            if (IndustrialLookup.HasComponent(entity)) return (int)BuildingCategory.Industrial;
            if (OfficeLookup.HasComponent(entity)) return (int)BuildingCategory.Office;
            return (int)BuildingCategory.Services;
        }

        private bool ShouldBlackout(int districtIndex, int categoryId, Entity entity)
        {
            // VIP districts NEVER blackout (protected from schedule AND load shedding)
            if (VIPDistricts.ContainsKey(districtIndex))
                return false;

            // VIP Bypass: Wealthy households never blackout in protected districts
            if (VIPBypass.ContainsKey(districtIndex))
            {
                if (HasWealthyRenter(entity))
                    return false;
            }

            // Check schedule-based blackout (with phase offset)
            // Per-district override takes priority; city-wide schedule is fallback
            int scheduleId = 0;
            bool hasSchedule = DistrictSchedules.TryGetValue(districtIndex, out scheduleId);
            if (!hasSchedule && CityScheduleId > 0)
            {
                scheduleId = CityScheduleId;
                hasSchedule = true;
            }

            if (hasSchedule && scheduleId > 0)
            {
                if (ScheduleHelper.IsBlackoutActive(scheduleId, GameHour, districtIndex))
                    return true;
            }

            // Check manual category blackout
            // BUG-BLK-005: Base-10 formula for district+category composite key
            int key = districtIndex * Engine.PowerGrid.CATEGORY_MULTIPLIER + categoryId;
            return CategoryBlackouts.ContainsKey(key);
        }

        /// <summary>
        /// Check if building has any Wealthy household renter.
        /// Wealth = Household.m_Resources + Resources[Money]
        /// </summary>
        private bool HasWealthyRenter(Entity buildingEntity)
        {
            if (!RenterLookup.HasBuffer(buildingEntity))
                return false;

            var renters = RenterLookup[buildingEntity];

            for (int i = 0; i < renters.Length; i++)
            {
                Entity renterEntity = renters[i].m_Renter;

                if (!HouseholdLookup.HasComponent(renterEntity))
                    continue;

                var household = HouseholdLookup[renterEntity];
                long wealth = household.m_Resources;

                // Add money from Resources buffer
                if (ResourcesLookup.HasBuffer(renterEntity))
                {
                    var resources = ResourcesLookup[renterEntity];
                    for (int j = 0; j < resources.Length; j++)
                    {
                        if (resources[j].m_Resource == Resource.Money)
                        {
                            wealth += resources[j].m_Amount;
                            break;
                        }
                    }
                }

                // Check if wealthy (>= threshold)
                if (wealth >= WealthyThreshold)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// T2-12 FIX: Policy-aware backup power check.
        /// Reserve: never exempt (batteries saved for Cold Start).
        /// CriticalOnly: only critical buildings (hospitals, schools) exempt.
        /// FullDischarge: any building with charge/fuel exempt.
        /// </summary>
        private bool HasBackupPowerAvailable(Entity entity, bool hasBatteryPriority)
        {
            // Reserve policy: batteries never provide blackout exemption
            if (Policy == BackupPolicy.Reserve) return false;

            // CriticalOnly policy: only critical infrastructure gets exemption
            if (Policy == BackupPolicy.CriticalOnly && !hasBatteryPriority) return false;

            if (!BackupPowerLinks.TryGetValue(BuildingRef.FromEntity(entity).Packed, out var modEntity))
                return false;
            if (modEntity == Entity.Null)
                return false;
            if (!BackupPowerLookup.TryGetComponent(modEntity, out var backup))
                return false;
            if (backup.Type == BackupPowerType.None) return false;
            return backup.Type == BackupPowerType.DieselGenerator
                ? backup.FuelHours > 0
                : backup.CurrentChargeWh > 0;
        }

    }
}
