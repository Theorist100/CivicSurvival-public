using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Camera-to-threat proximity data for 3D audio positioning.
    /// Split from ThreatStatsSingleton to enforce single-writer rule.
    ///
    /// Writer: ThreatMovementSystem (computes closest-to-camera during radar update)
    /// Readers: ThreatAudioOrchestrator (volume/pitch/position)
    ///
    /// NOTE: Serialized as transient cache — reset to no-threat sentinel on load.
    /// </summary>
    public struct ThreatCameraProximitySingleton : IComponentData, IEmptySerializable
    {
        /// <summary>Distance from camera to closest threat in meters (for audio volume)</summary>
        public float ClosestDistance;

        /// <summary>World position of the threat closest to camera (for 3D audio positioning)</summary>
        public float3 ClosestPosition;

        public static ThreatCameraProximitySingleton Default => new()
        {
            ClosestDistance = float.MaxValue
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default, new EnsureSingletonPolicy<ThreatCameraProximitySingleton>
            {
                EnsureShape = EnsureValidSentinel
            });
        }

        private static void EnsureValidSentinel(EntityManager em, Entity entity)
        {
            var value = em.GetComponentData<ThreatCameraProximitySingleton>(entity);
            if (value.ClosestDistance <= 0f || float.IsNaN(value.ClosestDistance) || float.IsInfinity(value.ClosestDistance))
                em.SetComponentData(entity, Default);
        }

        // IEmptySerializable marker: recalculated during radar update; on load it
        // starts from the no-threat sentinel (Default via EnsureExists/SetDefaults).

        public void SetDefaults() { this = Default; }
    }
}
