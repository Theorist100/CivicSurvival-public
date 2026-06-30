using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.Cognitive.Core.Jobs
{
    /// <summary>
    /// Builds all per-district lookup maps on a worker thread for ResolveHouseholdPsyJob.
    /// Eliminates main-thread sync points: EntityManager.GetBuffer triggers
    /// CompleteDependencyBeforeRW when a worker job writes to the same buffer.
    ///
    /// Builds:
    /// 1. DistrictBatteryCoverage map (from BackupPowerStateSingleton buffer)
    /// 2. IPSO district exposure map (from IPSOState buffer)
    /// 3. Internet disabled set (from SpotterCountermeasuresState buffer)
    ///
    /// ECS automatically chains dependencies:
    /// WriteSingletonJob/WriteIPSOStateJob → this job → ResolveHouseholdPsyJob (all on workers).
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public struct BuildMentalHealthLookupsJob : IJob
    {
        // ═══════ Inputs ═══════

        [ReadOnly] public BufferLookup<DistrictBatteryCoverage> CoverageLookup;
        public Entity CoverageEntity;

        [ReadOnly] public BufferLookup<IPSODistrictExposureBuffer> IPSOLookup;
        public Entity IPSOEntity;

        [ReadOnly] public BufferLookup<InternetDisabledBuffer> InternetLookup;
        public Entity InternetEntity;

        // ═══════ Outputs ═══════

        public NativeHashMap<int, DistrictBatteryCoverage> CoverageMap;
        public NativeHashMap<int, float> IPSOExposureMap;
        public NativeParallelHashSet<int> InternetDisabledSet;

        public void Execute()
        {
            // Clear persistent containers (reused across frames)
            CoverageMap.Clear();
            IPSOExposureMap.Clear();
            InternetDisabledSet.Clear();

            // Build district battery coverage map
            if (CoverageLookup.HasBuffer(CoverageEntity))
            {
                var buffer = CoverageLookup[CoverageEntity];
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (!CoverageMap.TryAdd(buffer[i].DistrictIndex, buffer[i]))
                        CoverageMap[buffer[i].DistrictIndex] = buffer[i];
                }
            }

            // Build IPSO per-district exposure map
            if (IPSOLookup.HasBuffer(IPSOEntity))
            {
                var buffer = IPSOLookup[IPSOEntity];
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (!IPSOExposureMap.TryAdd(buffer[i].DistrictIndex, buffer[i].Exposure))
                        IPSOExposureMap[buffer[i].DistrictIndex] = buffer[i].Exposure;
                }
            }

            // Build internet disabled district set
            if (InternetLookup.HasBuffer(InternetEntity))
            {
                var buffer = InternetLookup[InternetEntity];
                for (int i = 0; i < buffer.Length; i++)
                    InternetDisabledSet.Add(buffer[i].DistrictIndex);
            }
        }
    }
}
