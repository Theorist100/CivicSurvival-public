using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    /// <summary>
    /// Cached building data for obstacle avoidance checks.
    /// Stored in NativeList for Burst-compatible spatial queries.
    /// </summary>
    public struct CachedBuilding
    {
        public BuildingRef Building;
        public float3 Center;
        public float3 Size;
        public float Height;
        public float Radius;  // Max(Width, Depth) / 2
    }
}
