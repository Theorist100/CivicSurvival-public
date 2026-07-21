using System.Diagnostics.CodeAnalysis;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Cognitive;

namespace CivicSurvival.Domains.Cognitive.Threats.Jobs
{
    /// <summary>
    /// Writes IPSOState singleton + IPSODistrictExposureBuffer on a worker thread.
    /// Eliminates main-thread sync point: SystemAPI.GetComponentRW triggers
    /// CompleteDependencyBeforeRW which stalls waiting for MentalHealth's parallel job.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    [SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "IPSO = domain acronym")]
    public struct WriteIPSOStateJob : IJob
    {
        public ComponentLookup<IPSOState> StateLookup;
        public BufferLookup<IPSODistrictExposureBuffer> ExposureLookup;
        public Entity SingletonEntity;

        // Computed values
        public float BaseIntensity;
        public float PostWaveSpikeTimer;
        public float GlobalExposure;
        public int AffectedDistrictCount;
        public int TotalDistrictCount;

        [ReadOnly]
        public NativeArray<IPSODistrictExposureBuffer> ExposureData;

        public void Execute()
        {
            if (!StateLookup.HasComponent(SingletonEntity))
                return;

            var current = StateLookup[SingletonEntity];

            StateLookup[SingletonEntity] = new IPSOState
            {
                IsActive = current.IsActive,
                BaseIntensity = BaseIntensity,
                GlobalExposure = GlobalExposure,
                AffectedDistrictCount = AffectedDistrictCount,
                TotalDistrictCount = TotalDistrictCount,
                PostWaveSpikeTimer = PostWaveSpikeTimer
            };

            if (!ExposureLookup.HasBuffer(SingletonEntity))
                return;

            var buffer = ExposureLookup[SingletonEntity];
            buffer.Clear();
            for (int i = 0; i < ExposureData.Length; i++)
                buffer.Add(ExposureData[i]);
        }
    }

    public struct ApplyIpsoEventJob : IJob
    {
        public ComponentLookup<IPSOState> StateLookup;
        public BufferLookup<IPSODistrictExposureBuffer> ExposureLookup;
        public Entity SingletonEntity;

        public bool SetActive;
        public bool IsActive;
        public bool SetPostWaveSpikeTimer;
        public float PostWaveSpikeTimer;
        public bool ClearExposure;

        public void Execute()
        {
            if (!StateLookup.HasComponent(SingletonEntity))
                return;

            var current = StateLookup[SingletonEntity];

            if (SetActive)
            {
                current.IsActive = IsActive;
                if (!IsActive)
                {
                    current.GlobalExposure = 0f;
                    current.AffectedDistrictCount = 0;
                    current.TotalDistrictCount = 0;
                    current.PostWaveSpikeTimer = 0f;
                }
            }

            if (SetPostWaveSpikeTimer && current.IsActive)
                current.PostWaveSpikeTimer = PostWaveSpikeTimer;

            StateLookup[SingletonEntity] = current;

            if (ClearExposure && ExposureLookup.HasBuffer(SingletonEntity))
                ExposureLookup[SingletonEntity].Clear();
        }
    }
}
