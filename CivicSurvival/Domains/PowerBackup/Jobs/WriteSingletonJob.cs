using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.PowerBackup.Jobs
{
    /// <summary>
    /// Writes BackupPowerStateSingleton + DistrictBatteryCoverage buffer on a worker thread.
    /// Eliminates main-thread sync point: EntityManager.SetComponentData triggers
    /// CompleteDependencyBeforeRW which stalls waiting for any job reading the same type.
    /// By moving the write to IJob, ECS chains dependencies automatically on worker threads.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public struct WriteSingletonJob : IJob
    {
        public ComponentLookup<BackupPowerStateSingleton> SingletonLookup;
        public BufferLookup<DistrictBatteryCoverage> CoverageLookup;
        public Entity SingletonEntity;

        public float ChargePercent;
        public int ProtectedBuildings;
        public int TotalCapacityKWh;
        public int DischargingCount;
        public int HospitalsPowered;
        public int HospitalsTotal;
        public int SchoolsPowered;
        public int SchoolsTotal;

        [ReadOnly]
        public NativeArray<DistrictBatteryCoverage> CoverageData;

        public void Execute()
        {
            if (!SingletonLookup.HasComponent(SingletonEntity))
                return;

            // Preserve Policy from current state (written by SettingsRequestSystem)
            var current = SingletonLookup[SingletonEntity];

            SingletonLookup[SingletonEntity] = new BackupPowerStateSingleton
            {
                ChargePercent = ChargePercent,
                ProtectedBuildings = ProtectedBuildings,
                TotalCapacityKWh = TotalCapacityKWh,
                DischargingCount = DischargingCount,
                Policy = current.Policy,
                HospitalsPowered = HospitalsPowered,
                HospitalsTotal = HospitalsTotal,
                SchoolsPowered = SchoolsPowered,
                SchoolsTotal = SchoolsTotal
            };

            if (!CoverageLookup.HasBuffer(SingletonEntity))
                return;

            var buffer = CoverageLookup[SingletonEntity];
            buffer.Clear();
            for (int i = 0; i < CoverageData.Length; i++)
                buffer.Add(CoverageData[i]);
        }
    }
}
