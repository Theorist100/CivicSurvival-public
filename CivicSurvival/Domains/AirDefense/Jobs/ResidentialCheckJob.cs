using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Jobs
{
    /// <summary>
    /// Burst-compiled parallel job for batch residential proximity check.
    /// Runs ONCE per targeting frame for all unique threat positions.
    ///
    /// Complexity: O(Threats × Residential) - done once, not per candidate.
    /// Results are reused by EngagementScoringJob via O(1) lookup.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct ResidentialCheckJob : IJobParallelForDefer
    {
        // Position read directly from ThreatData (no separate positions buffer) — index-parallel
        // with the deferred threat list the match job iterates.
        [ReadOnly] public NativeArray<ThreatData> Threats;
        [ReadOnly] public NativeArray<float3> ResidentialPositions;
        public float RadiusSquared;

        /// <summary>R9-L15: From BalanceConfig.AirDefense.BallisticSkipAltitude (set at schedule time).</summary>
        public float BallisticSkipAltitude;

        [WriteOnly] public NativeArray<bool> Results;

        public void Execute(int index)
        {
            float3 threatPos = Threats[index].Position;

            // High-altitude threats (ballistics) cannot cause residential debris — 2D check is invalid above threshold
            if (threatPos.y > BallisticSkipAltitude)
            {
                Results[index] = false;
                return;
            }

            float2 pos2D = new float2(threatPos.x, threatPos.z);

            for (int r = 0; r < ResidentialPositions.Length; r++)
            {
                float3 residentialPos = ResidentialPositions[r];
                float2 buildingPos = new float2(residentialPos.x, residentialPos.z);

                if (math.distancesq(pos2D, buildingPos) <= RadiusSquared)
                {
                    Results[index] = true;
                    return;
                }
            }

            Results[index] = false;
        }
    }
}
