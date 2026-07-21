using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.AirDefense.Jobs
{
    /// <summary>
    /// Async threat collector. Replaces the main-thread SystemAPI.Query loop that used to
    /// live in AirDefenseOrchestrator.CollectThreatData() (which drained the moving TMS
    /// graph on the main thread every scheduling tick — the 16-35ms ADO.CollectThreat spike).
    /// Reads Shahed/ShahedCombatState/ThreatPosition/Entity from chunks on a Burst worker and
    /// emits one ThreatData record per live, non-intercepted drone.
    ///
    /// Filter is identical to the former main-thread loop (m_ThreatQuery: ActiveThreat enabled,
    /// !Deleted, !PendingDestruction-enabled) plus the IsIntercepted skip, so the targeting set
    /// is unchanged.
    ///
    /// SCHEDULE CONTRACT: scheduled via ScheduleParallel(m_ThreatQuery, dependency) on the
    /// system-level Dependency. The moving writers (TMS movement graph) reach this job through
    /// AirDefenseOrchestrator's reader/writer lists, NOT through the schedule call: the query
    /// registers Shahed/ThreatPosition/ActiveThreat (RO) and the orchestrator's lookups register
    /// ShahedCombatState (m_CombatStateLookup) and PriorityTarget (m_PriorityTargetLookup). Those
    /// lookups must stay registered or the moving writers fall out of the incoming Dependency and
    /// this job races them = data race / native AV.
    ///
    /// Output capacity is sized by the orchestrator to chunkCount * 128 (hard chunk-capacity
    /// ceiling) before scheduling, so AddNoResize never overruns.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    internal struct CollectThreatJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Shahed> ShahedHandle;
        [ReadOnly] public ComponentTypeHandle<ShahedCombatState> CombatHandle;
        [ReadOnly] public ComponentTypeHandle<ThreatPosition> PositionHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        // PriorityTarget is read by entity (matches the former main-thread HasComponent call).
        [ReadOnly] public ComponentLookup<PriorityTarget> PriorityLookup;

        // Single output: ThreatData carries Position, so residential reads ThreatData.Position
        // directly — no separate positions buffer.
        public NativeList<ThreatData>.ParallelWriter Threats;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var shaheds = chunk.GetNativeArray(ref ShahedHandle);
            var combats = chunk.GetNativeArray(ref CombatHandle);
            var positions = chunk.GetNativeArray(ref PositionHandle);
            var entities = chunk.GetNativeArray(EntityHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (enumerator.NextEntityIndex(out int i))
            {
                if (combats[i].IsIntercepted) continue;

                var shahed = shaheds[i];
                var entity = entities[i];

                Threats.AddNoResize(new ThreatData
                {
                    EntityIndex = entity.Index,
                    EntityVersion = entity.Version,
                    Position = positions[i].Position,
                    DistanceToTarget = shahed.TotalDistance - shahed.CurrentDistance,
                    Category = shahed.TargetCategory,
                    MissedShots = combats[i].MissedShotsCount,
                    IsPriority = PriorityLookup.HasComponent(entity)
                });
            }
        }
    }
}
