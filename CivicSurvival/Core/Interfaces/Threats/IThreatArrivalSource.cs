using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Interfaces.Threats
{
    /// <summary>
    /// Provides pre-filtered threat arrival data for TAS consumption.
    /// TMS fills the list during Apply loop (Shahed) and after ballistic job Complete.
    /// TAS reads this list instead of querying Shahed/ThreatPosition/Ballistic —
    /// zero sync points guaranteed (no component queries in TAS).
    ///
    /// Contract: producer fills, consumer reads via ArrivedThreats (read-only view),
    /// then calls ConsumeAndClear() after processing. The normal consumer drains in
    /// OnUpdate; OnStopRunning may drain leftovers during lifecycle shutdown.
    /// Null-object: ArrivedThreats = default(NativeArray.ReadOnly) (IsCreated=false,
    /// foreach over yields zero iterations), ArrivalCount=0, IsCreated=false,
    /// ConsumeAndClear is no-op.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ThreatFlightName)]
    public interface IThreatArrivalSource
    {
        /// <summary>
        /// Read-only view of all threat arrivals detected this frame (Shahed + Ballistic).
        /// Filled by TMS during Apply (Shahed) and after ballistic Complete.
        /// Consumer must call <see cref="ConsumeAndClear"/> after processing.
        /// </summary>
        NativeArray<ThreatArrivalInfo>.ReadOnly ArrivedThreats { get; }

        /// <summary>Number of arrivals in the current batch.</summary>
        int ArrivalCount { get; }

        /// <summary>Whether the underlying buffer has been allocated.</summary>
        bool IsCreated { get; }

        /// <summary>
        /// Signals that the consumer has finished processing arrivals.
        /// Clears the underlying buffer. Single-consumer drain; calling on an empty buffer is a no-op.
        /// </summary>
        void ConsumeAndClear();
    }

    public struct ThreatArrivalInfo
    {
        public Entity Entity;
        /// <summary>Position at moment of arrival (for debris spawn / effects).</summary>
        public float3 Position;
        /// <summary>Intended target position (for impact events).</summary>
        public float3 TargetPosition;
        /// <summary>True = reached target (direct hit). False = exhausted/crashed.</summary>
        public bool IsHit;
        /// <summary>True = ballistic missile, false = Shahed drone.</summary>
        public bool IsBallistic;
        /// <summary>Per-threat ballistic blast radius; ignored for Shahed direct hits.</summary>
        public float ImpactRadius;
        /// <summary>Per-threat ballistic damage severity; ignored for Shahed direct hits.</summary>
        public float DamageSeverity;
        /// <summary>
        /// Threat generation copied from the arriving threat component. 0 =
        /// unstamped/invalid. Threaded to <see cref="ThreatImpactData"/> /
        /// <see cref="FallingDebris"/> so the impact consumer drops stale generations.
        /// </summary>
        public int ThreatGeneration;

        /// <summary>Burst-safe factory for a Shahed arrival — requires the threat's generation.</summary>
        public static ThreatArrivalInfo FromShahed(Entity entity, float3 position, float3 targetPosition, bool isHit, int threatGeneration) =>
            new() { Entity = entity, Position = position, TargetPosition = targetPosition, IsHit = isHit, IsBallistic = false, ThreatGeneration = threatGeneration };

        /// <summary>Burst-safe factory for a ballistic arrival — requires the threat's generation.</summary>
        public static ThreatArrivalInfo FromBallistic(
            Entity entity,
            float3 position,
            float3 targetPosition,
            bool isHit,
            int threatGeneration,
            float impactRadius,
            float damageSeverity) =>
            new()
            {
                Entity = entity,
                Position = position,
                TargetPosition = targetPosition,
                IsHit = isHit,
                IsBallistic = true,
                ImpactRadius = impactRadius,
                DamageSeverity = damageSeverity,
                ThreatGeneration = threatGeneration
            };
    }

    /// <summary>
    /// Provides completed ballistic kinematics for cross-domain consumers.
    /// TMS refreshes this only after its ballistic movement job is complete, so readers
    /// never touch Ballistic/ThreatPosition while those components may have pending writers.
    /// Returned views are valid only for the current OnUpdate; TMS clears and repopulates
    /// the backing list on its next tick, so consumers must not retain the NativeArray view.
    /// Null-object: empty default NativeArray.ReadOnly, BallisticCount=0,
    /// IsBallisticSnapshotCreated=false — foreach over yields zero iterations.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ThreatFlightName)]
    public interface IBallisticSnapshotSource
    {
        NativeArray<BallisticSnapshotInfo>.ReadOnly BallisticSnapshots { get; }
        int BallisticCount { get; }
        bool IsBallisticSnapshotCreated { get; }
    }

    public struct BallisticSnapshotInfo
    {
        public Entity Entity;
        public float3 Position;
        public bool IsArrived;
        public bool IsIntercepted;
        /// <summary>
        /// Threat generation copied from the ballistic component. 0 =
        /// unstamped/invalid. BallisticDefenseSystem drops a stale-generation
        /// snapshot before spending ammo / scheduling an intercept.
        /// </summary>
        public int ThreatGeneration;

        /// <summary>Burst-safe factory — requires the ballistic's threat generation.</summary>
        public static BallisticSnapshotInfo From(Entity entity, float3 position, bool isArrived, bool isIntercepted, int threatGeneration) =>
            new() { Entity = entity, Position = position, IsArrived = isArrived, IsIntercepted = isIntercepted, ThreatGeneration = threatGeneration };
    }
}
