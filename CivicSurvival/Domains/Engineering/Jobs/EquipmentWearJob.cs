using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Logic;

namespace CivicSurvival.Domains.Engineering.Jobs
{
    /// <summary>
    /// Status after wear calculation.
    /// </summary>
    public enum WearStatus : byte
    {
        Normal = 0,
        ShouldExplode,
        RepairComplete
    }

    /// <summary>
    /// Input data for equipment wear calculation.
    /// </summary>
    public struct EquipmentWearInput
    {
        public float WearPercent;
        public float OverloadHours;
        public bool HasExploded;
        public bool IsUnderRepair;
        public float RepairEndHour;
        public byte RepairEpoch;
        public int Capacity;
        public uint RandomSeed;
    }

    /// <summary>
    /// Output data from equipment wear calculation.
    /// </summary>
    public struct EquipmentWearOutput
    {
        public float NewWearPercent;
        public float NewOverloadHours;
        public byte RepairEpoch;
        public WearStatus Status;
    }

    /// <summary>
    /// Configuration for equipment wear calculation.
    /// Cached once before job to avoid managed access.
    /// </summary>
    public struct EquipmentWearConfig
    {
        public float HighLoadThreshold;     // 0.9 = 90%
        public float OverloadThreshold;     // 1.0 = 100%
        public float BaseWearRate;          // % per hour at high load
        public float OverloadMultiplier;    // Multiplier when overloaded
        public float MaxWearPercent;        // Cap at 100%
        public float DangerZoneThreshold;   // 0.5 = 50% wear
        public float MaxExplosionRisk;      // 0.05 = 5% per hour at 100% wear
    }

    /// <summary>
    /// Burst-compiled parallel job for equipment wear calculations.
    /// Handles wear accumulation, explosion probability, and repair completion checks.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct EquipmentWearJob : IJobParallelFor
    {
        // Shared parameters
        [ReadOnly] public float DeltaHours;
        [ReadOnly] public float CityLoadRatio;
        [ReadOnly] public float GameHour;
        [ReadOnly] public EquipmentWearConfig Config;

        // Input (read-only)
        [ReadOnly] public NativeArray<EquipmentWearInput> Inputs;

        // Output (write per-index)
        [WriteOnly] public NativeArray<EquipmentWearOutput> Outputs;

        public void Execute(int index)
        {
            var input = Inputs[index];
            var output = new EquipmentWearOutput
            {
                NewWearPercent = input.WearPercent,
                NewOverloadHours = input.OverloadHours,
                RepairEpoch = input.RepairEpoch,
                Status = WearStatus.Normal
            };

            // --- 1. CHECK REPAIR COMPLETION ---
            // Must run BEFORE capacity check: collapsed plants (Capacity=0) can still finish repairs
            if (input.IsUnderRepair && GameHour >= input.RepairEndHour)
            {
                output.Status = WearStatus.RepairComplete;
                Outputs[index] = output;
                return;
            }

            // Skip plants with no capacity (collapsed/disabled) — no wear, no explosion
            if (input.Capacity <= 0)
            {
                Outputs[index] = output;
                return;
            }

            // --- 2. UPDATE WEAR ---
            // Skip wear accumulation for plants under repair
            if (CityLoadRatio > 0f && !input.IsUnderRepair)
            {
                float wearRate = WearMath.WearRate(
                    CityLoadRatio,
                    Config.HighLoadThreshold,
                    Config.OverloadThreshold,
                    Config.BaseWearRate,
                    Config.OverloadMultiplier);

                // Overload also accrues overload-hours; track it alongside the rate.
                if (CityLoadRatio > Config.OverloadThreshold)
                {
                    output.NewOverloadHours = input.OverloadHours + DeltaHours;
                }

                if (wearRate > 0f)
                {
                    output.NewWearPercent = WearMath.AccumulateWear(
                        input.WearPercent, wearRate, DeltaHours, Config.MaxWearPercent);
                }
            }

            // --- 3. CHECK EXPLOSION ---
            bool isOverloaded = CityLoadRatio > Config.OverloadThreshold;
            bool isInDangerZone = output.NewWearPercent >= Config.DangerZoneThreshold;

            if (isOverloaded && !input.HasExploded && !input.IsUnderRepair && isInDangerZone)
            {
                // Calculate explosion risk (linear from 0% at 50% wear to MaxExplosionRisk at 100% wear)
                float explosionRiskPerHour = WearMath.ExplosionRiskPerHour(
                    output.NewWearPercent,
                    Config.DangerZoneThreshold,
                    Config.MaxWearPercent,
                    Config.MaxExplosionRisk);
                float explosionChance = WearMath.ExplosionChance(explosionRiskPerHour, DeltaHours);

                // Use per-plant random seed for deterministic but varied results
                var random = new Unity.Mathematics.Random(input.RandomSeed);
                if (random.NextFloat() < explosionChance)
                {
                    output.Status = WearStatus.ShouldExplode;
                }
            }

            Outputs[index] = output;
        }
    }
}
