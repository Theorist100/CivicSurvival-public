using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    /// <summary>
    /// One collected drone: movement input + identity + radar payload, produced by
    /// <see cref="CollectShahedInputsJob"/> in a single Burst pass. Entity and its input
    /// live in one record so index-parallelism is structural — apply and radar read the
    /// same slot, no two-list drift to keep in sync.
    /// </summary>
    public struct ShahedCollectedInput
    {
        public Entity Entity;
        public ShahedMovementInput Input;
        public int MissedShotsCount;
        public bool IsIdentified;
    }

    /// <summary>
    /// Sizes the collect back-buffer to the live Shahed count inside the dependency chain
    /// (no main-thread sync). Mirrors ResidentPopulationModelSystem.ResizeBackContainersJob.
    /// Capacity (not Length): <see cref="CollectShahedInputsJob"/> appends via AddNoResize,
    /// so the backing buffer must be large enough before the parallel writers run.
    /// </summary>
    internal struct ResizeShahedCollectJob : Unity.Jobs.IJob
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        public NativeList<ShahedCollectedInput> Back;

        public void Execute()
        {
            Back.Clear();
            long target = math.max(256, (long)Entities.Length * 2);
            int targetInt = (int)math.min(target, int.MaxValue);
            if (Back.Capacity < targetInt) Back.Capacity = targetInt;
        }
    }

    /// <summary>
    /// Async drone-input collector. Replaces the main-thread ToArchetypeChunkArray
    /// materialisation that used to live in ThreatMovementSystem STEP 2 (which drained the
    /// ActiveThreat enableable write-dependency and materialised the chunk cache on the main
    /// thread every cycle). Reads the same five components from chunks on a Burst worker and
    /// emits one record per live, non-intercepted, non-arrived drone.
    /// Filter is identical to the former main-thread loop so radar/camera tracking keep the
    /// exact same drone set (R2 contract).
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    internal struct CollectShahedInputsJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Shahed> ShahedHandle;
        [ReadOnly] public ComponentTypeHandle<ShahedCombatState> CombatHandle;
        [ReadOnly] public ComponentTypeHandle<ThreatPosition> PositionHandle;
        [ReadOnly] public ComponentTypeHandle<ThreatFlightProgress> FlightProgressHandle;
        [ReadOnly] public ComponentTypeHandle<ActiveThreat> ActiveThreatHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        [ReadOnly] public ComponentLookup<IdentifiedTarget> IdentifiedLookup;

        public NativeList<ShahedCollectedInput>.ParallelWriter Output;

        /// <summary>
        /// Entity indices skipped for NaN/Inf position/speed/target. Burst can't call Log.*,
        /// so the offending index is queued and warned on the main thread after completion
        /// (Diagnostic-logs-policy: severity preserved as Warn, identity preserved).
        /// </summary>
        public NativeQueue<int>.ParallelWriter InvalidIndices;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var shaheds = chunk.GetNativeArray(ref ShahedHandle);
            var combatStates = chunk.GetNativeArray(ref CombatHandle);
            var positions = chunk.GetNativeArray(ref PositionHandle);
            var flightProgress = chunk.GetNativeArray(ref FlightProgressHandle);
            var activeMask = chunk.GetEnabledMask(ref ActiveThreatHandle);
            var entities = chunk.GetNativeArray(EntityHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                if (!activeMask.GetBit(i)) continue;

                var shahed = shaheds[i];
                // Coast gate (Burst): keep an awaiting (Patriot-intercepted) drone in the published
                // snapshot so it keeps moving + shows on radar until its interceptor arrives. Reads
                // AwaitingInterceptorImpact from the ShahedCombatState already loaded for this chunk —
                // no new ComponentLookup, no new sync point (Axiom 15).
                if ((combatStates[i].IsIntercepted && !combatStates[i].AwaitingInterceptorImpact)
                    || shahed.IsArrived) continue;

                float3 pos = positions[i].Position;
                Entity entity = entities[i];

                // NaN/Inf guard — keep bad data out of the movement job (NaN here becomes a
                // NaN delta → corrupt ThreatPosition → render/physics chaos). Record the
                // index for a main-thread Warn rather than dropping it silently.
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) ||
                    float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z) ||
                    float.IsNaN(shahed.Speed) || float.IsInfinity(shahed.Speed) || shahed.Speed <= 0f ||
                    math.any(math.isnan(shahed.TargetPosition)) || math.any(math.isinf(shahed.TargetPosition)))
                {
                    InvalidIndices.Enqueue(entity.Index);
                    continue;
                }

                var progress = flightProgress[i];
                Output.AddNoResize(new ShahedCollectedInput
                {
                    Entity = entity,
                    MissedShotsCount = combatStates[i].MissedShotsCount,
                    IsIdentified = IdentifiedLookup.HasComponent(entity)
                        && IdentifiedLookup[entity].Identified,
                    Input = new ShahedMovementInput
                    {
                        CurrentPosition = pos,
                        TargetPosition = shahed.TargetPosition,
                        SpawnPosition = shahed.SpawnPosition,
                        Speed = shahed.Speed,
                        CurrentDistance = shahed.CurrentDistance,
                        TotalDistance = shahed.TotalDistance,
                        TargetBuilding = shahed.TargetBuilding,
                        IsAvoiding = shahed.IsAvoiding,
                        AvoidanceWaypoint = shahed.AvoidanceWaypoint,
                        AvoidanceObstacle = shahed.AvoidanceObstacle,
                        PreviousAvoidanceObstacle = shahed.PreviousAvoidanceObstacle,
                        AvoidanceCooldown = shahed.AvoidanceCooldown,
                        TimeSinceCheckpoint = shahed.TimeSinceCheckpoint,
                        LastCheckpointPos = shahed.LastCheckpointPos,
                        MinDistanceToTarget = progress.MinDistanceToTarget,
                        MinDistanceTime = progress.MinDistanceTime,
                        CurrentDirection = shahed.CurrentDirection,
                        CurrentBankAngle = shahed.BankAngle
                    }
                });
            }
        }
    }
}
