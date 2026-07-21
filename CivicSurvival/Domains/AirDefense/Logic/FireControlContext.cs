using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Jobs;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Immutable input data for one frame of fire control.
    /// Collected by Orchestrator, consumed by FireControlExecutor.
    /// </summary>
    internal readonly struct FireControlContext
    {
        /// <summary>Scored engagement candidates (from ScoringJob, sorted descending by score).</summary>
        public readonly NativeArray<EngagementCandidate> ScoredCandidates;
        public readonly int CandidateCount;

        /// <summary>N-1 frame AA snapshot (read buffer after swap).</summary>
        public readonly NativeArray<AAData>.ReadOnly AAData;

        /// <summary>N-1 frame threat snapshot (read buffer after swap).</summary>
        public readonly NativeArray<ThreatData>.ReadOnly ThreatData;

        /// <summary>Spotter detection penalty (0-1).</summary>
        public readonly float SpotterPenalty;

        /// <summary>Telemarathon detection bonus.</summary>
        public readonly float DetectionBonus;

        /// <summary>Cached from BalanceConfig — endpoint epsilon for LOS raycast.</summary>
        public readonly float RaycastEpsilon;

        /// <summary>False positive chance for unidentified threats.</summary>
        public readonly float FalsePositiveChance;

        /// <summary>Accuracy bonus for identified targets.</summary>
        public readonly float IdentifiedTargetBonus;

        /// <summary>Height margin for LOS checks.</summary>
        public readonly float LOSHeightMargin;

        /// <summary>Threats above this altitude bypass LOS building checks (from BalanceConfig).</summary>
        public readonly float LOSAltitudeBypass;

        public FireControlContext(
            NativeArray<EngagementCandidate> scoredCandidates,
            int candidateCount,
            NativeArray<AAData>.ReadOnly aaData,
            NativeArray<ThreatData>.ReadOnly threatData,
            float spotterPenalty,
            float detectionBonus,
            float raycastEpsilon,
            float falsePositiveChance,
            float identifiedTargetBonus,
            float losHeightMargin,
            float losAltitudeBypass)
        {
            ScoredCandidates = scoredCandidates;
            CandidateCount = candidateCount;
            AAData = aaData;
            ThreatData = threatData;
            SpotterPenalty = spotterPenalty;
            DetectionBonus = detectionBonus;
            RaycastEpsilon = raycastEpsilon;
            FalsePositiveChance = falsePositiveChance;
            IdentifiedTargetBonus = identifiedTargetBonus;
            LOSHeightMargin = losHeightMargin;
            LOSAltitudeBypass = losAltitudeBypass;
        }
    }

    /// <summary>
    /// Output of fire control execution for one frame.
    /// All outputs are carried here — caller does not reach into FCE state.
    /// </summary>
    internal struct FireControlResult
    {
        /// <summary>AA shots fired this frame (excluding ballistic).</summary>
        public int ShotsFired;

        /// <summary>AA shots fired this frame broken down by firing AAType. Sums to ShotsFired.</summary>
        public AirDefenseShotsByType ShotsByType;

        /// <summary>
        /// ECB commands produced this frame (SetComponent + CreateEntity + AddComponent calls).
        /// Caller adds to its own EcbCommandCount counter via Interlocked.Add.
        /// Replaces AirDefenseOrchestrator.IncrementEcbCount() calls from within FCE.
        /// </summary>
        public int EcbCommands;

        /// <summary>
        /// R9-M5: ECB commands specifically for InterceptBarrier (intercept hits only).
        /// Caller uses this instead of ShotsFired for barrier registration — avoids
        /// unnecessary sync when all shots are misses/FP.
        /// </summary>
        public int InterceptCommands;

        /// <summary>
        /// Random state after all intercept rolls.
        /// Caller must persist this for next frame's Execute call.
        /// Replaces executor.Random property read-back.
        /// </summary>
        public SerializableRandom UpdatedRandom;
    }
}
