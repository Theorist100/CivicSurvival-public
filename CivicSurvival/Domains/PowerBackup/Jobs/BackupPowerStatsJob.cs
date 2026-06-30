using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.PowerBackup.Jobs
{
    /// <summary>
    /// Burst job for stats aggregation. Scheduled single-threaded after BackupPowerJob.
    /// Accumulates per chunk locally, then writes NativeReferences once per chunk.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct BackupPowerStatsJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<BackupPower> BackupPowerHandle;

#pragma warning disable CIVIC079 // Scheduled single-threaded via IJobChunk.Schedule; refs are chunk-local accumulators.
        public NativeReference<int> ProtectedBuildings;
        public NativeReference<int> DischargingCount;
        public NativeReference<int> GeneratorsRunning;
        public NativeReference<long> TotalCapacityWh;
        public NativeReference<long> TotalChargeWh;
        public NativeReference<int> GeneratorsTotal;
        public NativeReference<int> GeneratorsFueled;
#pragma warning restore CIVIC079

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var backups = chunk.GetNativeArray(ref BackupPowerHandle);

            int protectedBuildings = 0;
            int dischargingCount = 0;
            int generatorsRunning = 0;
            long totalCapacityWh = 0;
            long totalChargeWh = 0;
            int generatorsTotal = 0;
            int generatorsFueled = 0;

            for (int i = 0; i < backups.Length; i++)
            {
                var backup = backups[i];
                if (backup.Type == BackupPowerType.None)
                    continue;

                protectedBuildings++;

                if (backup.IsDischarging)
                {
                    dischargingCount++;
                    if (backup.Type == BackupPowerType.DieselGenerator)
                        generatorsRunning++;
                }

                if (backup.Type == BackupPowerType.DieselGenerator)
                {
                    generatorsTotal++;
                    if (backup.FuelHours > 0)
                        generatorsFueled++;
                }
                else
                {
                    // Single source of truth (includes degradation clamp); int result widens to long.
                    long effectiveCapacity = BackupPower.EffectiveCapacityWh(backup.CapacityWh, backup.Degradation);
                    totalCapacityWh += effectiveCapacity;
                    totalChargeWh += backup.CurrentChargeWh;
                }
            }

            ProtectedBuildings.Value += protectedBuildings;
            DischargingCount.Value += dischargingCount;
            GeneratorsRunning.Value += generatorsRunning;
            TotalCapacityWh.Value += totalCapacityWh;
            TotalChargeWh.Value += totalChargeWh;
            GeneratorsTotal.Value += generatorsTotal;
            GeneratorsFueled.Value += generatorsFueled;
        }
    }
}
