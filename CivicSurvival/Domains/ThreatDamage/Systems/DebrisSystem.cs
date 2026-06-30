using Game;
using Game.Common;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Debris fall timer — counts down TimeToImpact, generates impact at expiry.
    /// No live threat-position dependency — avoids TMS job chain sync point.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    [WithNone(typeof(Deleted))]
    public partial struct DebrisFallJob : IJobEntity
    {
        public float DeltaTime;
        public NativeList<ThreatImpactData> Impacts;
        public EntityCommandBuffer Ecb;
        public NativeReference<int> CommandCount;

        public void Execute(Entity entity, ref FallingDebris debris)
        {
            debris.TimeToImpact -= DeltaTime;

            if (debris.TimeToImpact <= 0f)
            {
                // C-5: factory carries the source threat generation through the debris hop.
                Impacts.Add(ThreatImpactData.FromDebris(debris.FallPosition, 0.5f, 0f, debris.ThreatGeneration));
                Ecb.AddComponent<Deleted>(entity);
                CommandCount.Value++;
            }
        }
    }

    /// <summary>
    /// Handles falling debris lifecycle — Burst IJobEntity, async pattern (1-frame delay).
    /// Frame N: schedule DebrisFallJob on worker thread.
    /// Frame N+1: Complete previous job, expose filled PendingDebrisImpacts for ThreatDamageSystem.
    /// No scheduling dependency on ThreatDamageSystem — fully decoupled.
    ///
    /// PERF: Query only FallingDebris (not live threat position) to avoid inheriting
    /// ThreatMovementSystem job chain in Dependency. This cut Complete() from 68ms to ~0ms.
    /// </summary>
    [ActIndependent]
    public partial class DebrisSystem : CivicSystemBase
    {
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);

        private static readonly LogContext Log = new("DebrisSystem");

        private EntityQuery m_DebrisQuery;
        private GameSimulationEndBarrier m_CleanupBarrier = null!;

        // Double-buffer: job writes to m_ActiveJobImpacts, TDS reads m_ReadyImpacts.
        // Swap on Complete — eliminates sync point in TDS (no need to Complete before read).
        private NativeList<ThreatImpactData> m_ReadyImpacts;
        private NativeList<ThreatImpactData> m_ActiveJobImpacts;
#pragma warning disable CIVIC150 // Ephemeral diagnostic counter — not serialized; save/load can lose at most one in-flight frame of counter telemetry
        private NativeReference<int> m_CommandCount;
#pragma warning restore CIVIC150
        private JobHandle m_PreviousJobHandle;
        private double m_NextCensusLogTime;

        /// <summary>
        /// Debris impacts ready for TDS to drain. No active job writes to this buffer.
        /// </summary>
        public NativeList<ThreatImpactData> PendingDebrisImpacts => m_ReadyImpacts;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_DebrisQuery = GetEntityQuery(
                ComponentType.ReadWrite<FallingDebris>(),
                ComponentType.Exclude<Deleted>()
            );

            m_CleanupBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_ReadyImpacts = new NativeList<ThreatImpactData>(16, Allocator.Persistent);
            m_ActiveJobImpacts = new NativeList<ThreatImpactData>(16, Allocator.Persistent);
            m_CommandCount = new NativeReference<int>(Allocator.Persistent);

            Log.Info("Created (FallingDebris-only query, async Schedule)");
        }

        protected override void OnUpdateImpl()
        {
            // Complete previous frame's job — m_ActiveJobImpacts now safe
            using (Core.Utils.PerformanceProfiler.Measure("SP:Debris.Complete"))
            {
                m_PreviousJobHandle.Complete();
            }

            // Collect ECB count from previous job
            int prevCommands = m_CommandCount.Value;
            if (prevCommands > 0)
            {
                Interlocked.Add(ref s_EcbCommandCount, prevCommands);
                m_CommandCount.Value = 0;
            }

            // Double-buffer swap: m_ActiveJobImpacts (just completed) becomes m_ReadyImpacts for TDS.
            // Old m_ReadyImpacts (already drained by TDS) becomes the new job target.
            (m_ReadyImpacts, m_ActiveJobImpacts) = (m_ActiveJobImpacts, m_ReadyImpacts);
            m_ActiveJobImpacts.Clear();

            // DIAG: FallingDebris now lives on renderless timer entities. The count should
            // rise only transiently and then fall as timers expire.
            if (Log.IsDebugEnabled)
            {
                double nowCensus = SystemAPI.Time.ElapsedTime;
                if (nowCensus >= m_NextCensusLogTime)
                {
                    m_NextCensusLogTime = nowCensus + 2.0d;
                    Log.Debug($"[THREAT-CENSUS] fallingDebrisTimers={m_DebrisQuery.CalculateEntityCountWithoutFiltering()}");
                }
            }

            if (m_DebrisQuery.IsEmptyIgnoreFilter)
                return;

            float deltaTime = SystemAPI.Time.DeltaTime;

            // DebrisFallJob writes m_ActiveJobImpacts; the buffer is swapped for
            // ThreatDamageSystem consumption on the next update before being cleared.
#pragma warning disable CIVIC187
            var job = new DebrisFallJob
            {
                DeltaTime = deltaTime,
                Impacts = m_ActiveJobImpacts,
                Ecb = m_CleanupBarrier.CreateCommandBuffer(),
                CommandCount = m_CommandCount
            };
#pragma warning restore CIVIC187

            // Schedule (not ScheduleParallel): CommandCount.Value++ is non-atomic.
            // Compute is trivial (1 float op per entity) — single thread sufficient for 1000+ entities.
            // No live threat-position component in query → Dependency chain excludes TMS jobs → Complete ~0ms.
            using (Core.Utils.PerformanceProfiler.Measure("SP:Debris.Schedule"))
            {
                // IsEmpty (not CalculateEntityCount) — count forces a sync point that would skew the
                // very timing race we're hunting (CIVIC220 / Heisenbug). Bool is enough for the marker.
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre DebrisFallJob.Schedule empty={m_DebrisQuery.IsEmpty} impacts={m_ActiveJobImpacts.IsCreated}/{m_ActiveJobImpacts.Length} commands={m_CommandCount.IsCreated}");
                m_PreviousJobHandle = job.Schedule(m_DebrisQuery, Dependency);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post DebrisFallJob.Schedule impacts={m_ActiveJobImpacts.IsCreated}/{m_ActiveJobImpacts.Length} commands={m_CommandCount.IsCreated}");
                Dependency = m_PreviousJobHandle;
            }

            m_CleanupBarrier.AddJobHandleForProducer(Dependency);
        }

        protected override void OnStopRunning()
        {
            m_PreviousJobHandle.Complete();
            // Swap so final impacts in m_ActiveJobImpacts become available to TDS via m_ReadyImpacts.
            // ThreatDamageDomain registers ThreatArrival -> Debris -> ThreatDamageIntake,
            // so ThreatDamageSystem drains m_ReadyImpacts after this producer in the same frame.
            // Without swap, last debris impacts of every wave are silently lost.
            (m_ReadyImpacts, m_ActiveJobImpacts) = (m_ActiveJobImpacts, m_ReadyImpacts);
            if (m_ActiveJobImpacts.IsCreated) m_ActiveJobImpacts.Clear();
            m_CommandCount.Value = 0;
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            m_PreviousJobHandle.Complete();
            if (m_ReadyImpacts.IsCreated) m_ReadyImpacts.Dispose();
            if (m_ActiveJobImpacts.IsCreated) m_ActiveJobImpacts.Dispose();
            if (m_CommandCount.IsCreated) m_CommandCount.Dispose();
            base.OnDestroy();
        }
    }
}
