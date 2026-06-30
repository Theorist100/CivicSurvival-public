using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Jobs
{
    /// <summary>
    /// Burst-compiled parallel job for matching threats to AA installations.
    /// For each threat, finds all AAs that have it in range.
    /// Output: list of (threatIndex, aaIndex, distanceSq) pairs.
    ///
    /// Complexity: O(threats × AAs) but fully parallel and Burst-compiled.
    /// For 50 threats × 20 AAs = 1000 distance checks, this is faster than
    /// sequential QuadTree traversal due to cache locality and SIMD.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct ThreatAAMatchJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<ThreatData> Threats;
        [ReadOnly] public NativeArray<AAData> AAs;

        // Thread-safe output: each thread writes to its own section
        public NativeList<EngagementCandidate>.ParallelWriter Candidates;

        public void Execute(int index)
        {
            var threat = Threats[index];
            float3 threatPos = threat.Position;

            for (int aaIdx = 0; aaIdx < AAs.Length; aaIdx++)
            {
                var aa = AAs[aaIdx];
                float distSq = math.distancesq(threatPos, aa.Position);

                if (distSq <= aa.RangeSq)
                {
                    Candidates.AddNoResize(new EngagementCandidate
                    {
                        ThreatIndex = index,
                        AAIndex = aaIdx,
                        DistanceSq = distSq,
                        Distance = math.sqrt(distSq), // computed once here, not per scoring frame
                        Score = 0f,
                        IsOverResidential = false,
                        PassedLOS = false, // L11: LOS not yet verified at match time — checked in FireControlExecutor
                        // S16a-1 FIX: Store entity identity for cross-frame validation
                        ThreatEntityIndex = threat.EntityIndex,
                        ThreatEntityVersion = threat.EntityVersion,
                        // Cache at match-time to avoid re-fetch in FireControlExecutor
                        InterceptChance = aa.InterceptChance,
                        CooldownDuration = aa.CooldownDuration
                    });
                }
            }
        }
    }
}
