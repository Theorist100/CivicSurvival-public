using System.Diagnostics.CodeAnalysis;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Enemy IPSO (information-psychological operations) campaign state singleton.
    /// Models enemy propaganda: bot farms, leaflets, telegram channels.
    ///
    /// Access: SystemAPI.GetSingleton&lt;IPSOState&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;IPSOState&gt;()
    ///
    /// Writer: IPSOCampaignSystem
    /// Readers: MentalHealthResolverSystem (builds NativeHashMap for job), UI panels
    ///
    /// Buffers attached: IPSODistrictExposureBuffer
    /// </summary>
    [SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "IPSO = Information and Psychological Operations, domain acronym")]
    public struct IPSOState : IComponentData
    {
        /// <summary>Whether IPSO campaign is active (Crisis act or later).</summary>
        public bool IsActive;

        /// <summary>Base intensity from wave progression, 0-0.8. Wave 1 = 0.15, Wave 14+ = 0.80.</summary>
        public float BaseIntensity;

        /// <summary>Average exposure across all districts (for UI, 0-1).</summary>
        public float GlobalExposure;

        /// <summary>Number of districts with exposure > 0.1 (for UI).</summary>
        public int AffectedDistrictCount;

        /// <summary>Total tracked districts (for UI "6/12" display).</summary>
        public int TotalDistrictCount;

        /// <summary>Remaining game-time seconds of post-wave intensity spike (×1.5 multiplier); counts down with game speed, frozen on pause.</summary>
        public float PostWaveSpikeTimer;

        public static IPSOState Default => new()
        {
            IsActive = false,
            BaseIntensity = 0f,
            GlobalExposure = 0f,
            AffectedDistrictCount = 0,
            TotalDistrictCount = 0,
            PostWaveSpikeTimer = 0f
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default, new EnsureSingletonPolicy<IPSOState>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<IPSODistrictExposureBuffer>(entity))
                em.AddBuffer<IPSODistrictExposureBuffer>(entity);
        }
    }

    /// <summary>
    /// Per-district IPSO exposure value.
    /// Attached to IPSOState singleton entity.
    /// Read by MentalHealthResolverSystem to build NativeHashMap for ResolveHouseholdPsyJob.
    /// </summary>
    [InternalBufferCapacity(16)]
    [SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "IPSO = Information and Psychological Operations, domain acronym")]
    public struct IPSODistrictExposureBuffer : IBufferElementData
    {
        /// <summary>District entity index.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>IPSO exposure intensity for this district (0-1).</summary>
        public float Exposure;
    }
}
