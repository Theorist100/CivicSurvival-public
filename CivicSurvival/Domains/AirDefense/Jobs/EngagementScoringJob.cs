using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Game.Objects;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Logic;

namespace CivicSurvival.Domains.AirDefense.Jobs
{
    /// <summary>
    /// Burst-compiled parallel job for scoring engagement candidates.
    /// Uses pre-computed residential proximity results (O(1) lookup per candidate).
    ///
    /// Performance: O(Candidates) instead of O(Candidates × Residential).
    /// Residential check is done ONCE per threat by ResidentialCheckJob.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct EngagementScoringJob : IJobParallelFor
    {
        // ===== Engagement Score Constants =====
        private const float PRIORITY_TARGET_BONUS = 500f;

        [ReadOnly] public NativeArray<ThreatData> Threats;
        // A-02 FIX: Removed unused AAs field (scoring uses Threats, not AA data)
        [ReadOnly] public NativeArray<bool> ThreatIsOverResidential; // Pre-computed per threat
        public ScoringConfig Config;

        // In-place update of candidates with score
        public NativeArray<EngagementCandidate> Candidates;
        public int CandidateCount; // actual count the job was scheduled for

        public void Execute(int index)
        {
            // Bounds check for deferred N-1 count scheduling
            // (scheduled with last frame's count, current frame may have fewer candidates)
            // Candidates.Length = CandidateCount + 32 slack — check both: CandidateCount is the
            // logical limit, Candidates.Length guards against scheduling/realloc race.
            if (index >= CandidateCount || index >= Candidates.Length)
                return;

            var c = Candidates[index];

            // SAFETY: Validate ThreatIndex bounds before access; use a negative sentinel
            // so score 0 remains a valid floor for low-priority targets.
            if (c.ThreatIndex < 0 || c.ThreatIndex >= Threats.Length)
            {
                c.Score = AirDefenseScoringRules.InvalidScore;
                Candidates[index] = c;
                return;
            }

            var threat = Threats[c.ThreatIndex];

            // S16a-1 FIX: Cross-frame entity validation — skip if threat at this index
            // is a different entity than when the candidate was generated (N-1 frame)
            if (threat.EntityIndex != c.ThreatEntityIndex || threat.EntityVersion != c.ThreatEntityVersion)
            {
                c.Score = AirDefenseScoringRules.InvalidScore;
                Candidates[index] = c;
                return;
            }

            // O(1) lookup instead of O(Residential) loop.
            // Default true when array not yet populated (ResidentialCheckJob cold start):
            // conservative safe assumption — HumanitarianShield policy stays active.
            bool isOverResidential = ThreatIsOverResidential.Length == 0
                || c.ThreatIndex >= ThreatIsOverResidential.Length  // M14: OOB → conservative true (new threat entering residential)
                || ThreatIsOverResidential[c.ThreatIndex];

            // --- Engagement score calculation ---
            // Use pre-computed Distance (sqrt done once at match-time)
            float score = AirDefenseScoringRules.CalculateEngagementScore(
                c.Distance,
                threat.DistanceToTarget,
                threat.Category,
                Config.Policy,
                isOverResidential,
                Config.CriticalDistance
            );

            // Player engagement pipeline: PriorityTarget → redirect AA
            if (threat.IsPriority)
            {
                score += PRIORITY_TARGET_BONUS;
            }

            c.Score = score;
            c.IsOverResidential = isOverResidential;
            Candidates[index] = c;
        }

    }
}
