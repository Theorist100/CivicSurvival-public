using Game.Buildings;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.PowerBackup.Jobs
{
    /// <summary>
    /// Burst job for backup power charge/discharge calculations.
    /// Processes BackupPower mod entities in parallel.
    /// Uses ComponentLookup to read ElectricityConsumer from vanilla building.
    /// Runs ASYNC - scheduled this frame, results applied next frame.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct BackupPowerJob : IJobEntity
    {
        public float DeltaHours;
        public float GeneratorEfficiency;
        public float GridPowerThreshold;
        public float DegradationPerHour;
        public float IdleDegradationFraction;
        public float CounterfeitIdlePenalty;
        // Frame-local map for CounterfeitBattery by building index (mod entity isolation)
        [ReadOnly] public NativeHashMap<long, CounterfeitBattery> CounterfeitByBuilding;

        // Lookup for ElectricityConsumer on vanilla building
        [ReadOnly] public ComponentLookup<ElectricityConsumer> ConsumerLookup;

        // BUG-4 FIX: Discharge policy from BackupPowerStateSingleton
        public BackupPolicy Policy;

        // Single source of truth for battery priority (set at building creation by BlackoutStateSetupSystem)
        [ReadOnly] public ComponentLookup<BlackoutState> BlackoutStateLookup;

        // Three-layer: economy charge modulation for Private batteries
        [ReadOnly] public ComponentLookup<BatteryLayerTag> LayerTagLookup;
        public float PrivateChargeRateMultiplier;

        public void Execute(Entity modEntity, ref BackupPower backup)
        {
            if (backup.Type == BackupPowerType.None)
                return;

            // Get vanilla building from mod entity
            var buildingEntity = backup.GetBuildingEntity();

            // Get ElectricityConsumer from vanilla building
            if (!ConsumerLookup.TryGetComponent(buildingEntity, out var consumer))
            {
                backup.IsDischarging = false;
                return;
            }

            // Sanitize corrupted data
            // T2-13 FIX: Clamp to effective capacity (with degradation), not raw CapacityWh.
            // Without this, charge can exceed effective capacity after degradation increases.
            backup.CurrentChargeWh = math.max(0, backup.CurrentChargeWh);
            // Skip capacity clamp for generators — they are fuel-only and keep charge/capacity at 0.
            if (backup.Type != BackupPowerType.DieselGenerator)
                backup.CurrentChargeWh = math.min(backup.CurrentChargeWh, GetEffectiveCapacity(backup));

            // Determine layer for economy modulation
            bool isPrivate = true;
            if (LayerTagLookup.TryGetComponent(modEntity, out var layerTag))
            {
                isPrivate = layerTag.Layer == BatteryLayer.Private;
            }

            bool servedByBackup = IsServedByBackup(buildingEntity);

            // Check if building has grid power. A blackout exemption served by
            // backup leaves fulfilled load non-zero, so use the explicit
            // BlackoutState signal instead of treating it as grid supply.
            bool hasGridPower = !servedByBackup
                && consumer.m_FulfilledConsumption >= consumer.m_WantedConsumption * GridPowerThreshold;

            if (hasGridPower)
            {
                backup.IsDischarging = false;
                ChargeBackup(buildingEntity, ref backup, isPrivate);
            }
            else
            {
                backup.IsDischarging = DischargeBackup(buildingEntity, ref backup, consumer.m_WantedConsumption);
            }
        }

        private void ChargeBackup(Entity buildingEntity, ref BackupPower backup, bool isPrivate)
        {
            if (backup.Type == BackupPowerType.DieselGenerator)
                return;

            // Calendar aging: passive degradation during idle/charging
            float idleRate = DegradationPerHour * IdleDegradationFraction;
            long counterfeitKey = ((long)buildingEntity.Index << 32) | (uint)buildingEntity.Version;
            if (CounterfeitByBuilding.ContainsKey(counterfeitKey))
            {
                idleRate *= CounterfeitIdlePenalty;
            }
            backup.Degradation += DeltaHours * idleRate;
            backup.Degradation = math.clamp(backup.Degradation, 0f, BackupPower.MAX_DEGRADATION);

            int effectiveCapacity = GetEffectiveCapacity(backup);
            if (backup.CurrentChargeWh >= effectiveCapacity)
                return;

            float chargeWh = backup.ChargeRateW * DeltaHours * backup.Efficiency;

            // Economy modulation: private batteries charge slower during crisis
            if (isPrivate)
            {
                chargeWh *= PrivateChargeRateMultiplier;
            }

            long wholeCharge = GameRate.AccumulateWithRemainder(chargeWh, 1.0, ref backup.ChargeRemainder);
            int safeChargeWh = (int)math.min(wholeCharge, int.MaxValue);
            backup.CurrentChargeWh = math.min(backup.CurrentChargeWh + safeChargeWh, effectiveCapacity);
        }

        private bool DischargeBackup(Entity entity, ref BackupPower backup, int consumptionW)
        {
            // BUG-4 FIX: Check discharge policy BEFORE discharging
            switch (Policy)
            {
                case BackupPolicy.Reserve:
                    // Batteries saved for Cold Start — never discharge automatically
                    return false;

                case BackupPolicy.CriticalOnly:
                    // Only discharge for battery-priority buildings (hospital, school, fire, water)
                    if (!HasBatteryPriority(entity))
                        return false;
                    break;

                case BackupPolicy.FullDischarge:
                    // Discharge everything (original behavior)
                    break;
                default:
                    return false;
            }

            if (consumptionW <= 0)
                return false;

            // Generators are fuel-limited, not degradation-limited.
            // No operational wear (motor resource) — returns before degradation block.
            // Degradation only from fires (+0.1f in BackupPowerEffectsJob).
            // Revisit if fuel purchasing is added (generator becomes unlimited without second constraint).
            if (backup.Type == BackupPowerType.DieselGenerator)
            {
                if (backup.FuelHours > 0)
                {
                    backup.FuelHours -= DeltaHours * GeneratorEfficiency;
                    if (backup.FuelHours < 0) backup.FuelHours = 0;
                    return true;
                }
                return false;
            }

            if (backup.CurrentChargeWh <= 0)
                return false;

            int actualDischargeW = math.min(consumptionW, backup.DischargeRateW);
            if (actualDischargeW <= 0)
                return false;

            float dischargeWh = actualDischargeW * DeltaHours / math.max(backup.Efficiency, 0.01f);
            long wholeDischarge = GameRate.AccumulateWithRemainder(dischargeWh, 1.0, ref backup.DischargeRemainder);
            int safeDischargeWh = (int)math.min(wholeDischarge, int.MaxValue);
            backup.CurrentChargeWh = math.max(backup.CurrentChargeWh - safeDischargeWh, 0);

            // Apply degradation (with counterfeit multiplier if applicable)
            float degradationRate = DegradationPerHour;
            long dischCounterfeitKey = ((long)entity.Index << 32) | (uint)entity.Version;
            if (CounterfeitByBuilding.TryGetValue(dischCounterfeitKey, out var dischCounterfeit))
            {
                degradationRate *= dischCounterfeit.DegradationRate;
            }
            backup.Degradation += DeltaHours * degradationRate;
            backup.Degradation = math.clamp(backup.Degradation, 0f, BackupPower.MAX_DEGRADATION);
            return true;
        }

        /// <summary>
        /// Check if building qualifies for battery discharge under CriticalOnly policy.
        /// Reads HasBatteryPriority from BlackoutState (set once at creation by BlackoutStateSetupSystem).
        /// </summary>
        private bool HasBatteryPriority(Entity entity)
        {
            // Fail-safe: if BlackoutState absent (first tick after load), grant priority
            // to avoid cutting hospitals/schools. Over-discharge for 1 tick is safer.
            return !BlackoutStateLookup.TryGetComponent(entity, out var state) || state.HasBatteryPriority;
        }

        private bool IsServedByBackup(Entity entity)
        {
            return BlackoutStateLookup.TryGetComponent(entity, out var state) && state.ServedByBackup;
        }

        private static int GetEffectiveCapacity(BackupPower backup)
        {
            // Single source of truth (includes degradation clamp).
            return BackupPower.EffectiveCapacityWh(backup.CapacityWh, backup.Degradation);
        }
    }
}
