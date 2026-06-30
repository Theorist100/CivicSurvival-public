using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Singleton entity that owns ImpactDistrictEntry buffer.
    /// Written by: ThreatDamageSystem (appends per impact)
    /// Read+cleared by: MentalHealthResolverSystem (snapshot every 8 frames)
    ///
    /// Own singleton follows existing pattern:
    /// - BackupPowerStateSingleton owns DistrictBatteryCoverage
    /// - IPSOState owns IPSODistrictExposureBuffer
    /// - SpotterCountermeasuresState owns InternetDisabledBuffer
    /// </summary>
    [CivicSingleton]
    public struct ImpactPressureSingleton : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(ImpactPressureSingleton), new EnsureSingletonPolicy<ImpactPressureSingleton>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<ImpactDistrictEntry>(entity))
                em.AddBuffer<ImpactDistrictEntry>(entity);
        }
    }

    /// <summary>
    /// Buffer element: one recent impact's district + intensity.
    /// Ephemeral — snapshot+cleared by MHR every 8 frames, no serialization.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct ImpactDistrictEntry : IBufferElementData
    {
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        /// <summary>District index of impact location</summary>
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Impact intensity (0-1, clamped from ThreatImpactData.Severity)</summary>
        public float Intensity;

        public static ImpactDistrictEntry Create(int districtIndex, float intensity) => new()
        {
            // District identity is the raw entity index (Unzoned = 0); never a
            // small dense index. Only floor stray negatives to the Unzoned
            // bucket — the old 0..499 clamp mis-attributed every impact in a
            // district entity-index ≥500 to district 499 (G7 A-5).
            DistrictIndex = math.max(districtIndex, 0),
            Intensity = math.saturate(intensity)
        };
    }
}
