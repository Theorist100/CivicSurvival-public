using System.Collections.Generic;
using System.Threading;
using CivicSurvival.Core.Components.Domain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Constants;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    [ActIndependent]
    public partial class ThreatTerminalizationSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatTerminalizationSystem");
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void AddToEcbCount(int count) => Interlocked.Add(ref s_EcbCommandCount, count);

        private readonly List<ThreatTerminalOutcome> m_Outcomes = new(32);

        private ThreatLifecycleBarrier m_ThreatLifecycleBarrier = null!;
        [System.NonSerialized] private IThreatLifecycleDedup m_ThreatLifecycleDedup = null!;
        [System.NonSerialized] private IThreatTerminalizationSink m_TerminalizationQueue = null!;
        [System.NonSerialized] private IThreatAudioService m_AudioService = null!;
        [System.NonSerialized] private VanillaVfxSystem? m_VanillaVfx;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<ActiveThreat> m_ActiveThreatLookup;
        private ComponentLookup<PendingDestruction> m_PendingDestructionLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;

        private NativeList<ThreatImpactData> m_ProducerImpacts;
        private NativeList<ThreatImpactData> m_ConsumerImpacts;
        private bool m_ConsumerOwnsImpacts;

        public bool HasPendingImpacts => m_ProducerImpacts.IsCreated && m_ProducerImpacts.Length > 0;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ThreatLifecycleBarrier = World.GetOrCreateSystemManaged<ThreatLifecycleBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_ActiveThreatLookup = GetComponentLookup<ActiveThreat>(true);
            m_PendingDestructionLookup = GetComponentLookup<PendingDestruction>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_ProducerImpacts = new NativeList<ThreatImpactData>(32, Allocator.Persistent);
            m_ConsumerImpacts = new NativeList<ThreatImpactData>(32, Allocator.Persistent);
            ThreatOutcomeStatsSingleton.EnsureExists(EntityManager);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ResolveServices();
            ThreatOutcomeStatsSingleton.EnsureExists(EntityManager);
            if (SystemAPI.TryGetSingletonRW<ThreatOutcomeStatsSingleton>(out var statsRef))
                statsRef.ValueRW = ThreatOutcomeStatsSingleton.Default;
        }

        protected override void OnUpdateImpl()
        {
            ResolveServices();
            if (!m_TerminalizationQueue.HasPending)
                return;

            m_StorageInfoLookup.Update(this);
            m_ActiveThreatLookup.Update(this);
            m_PendingDestructionLookup.Update(this);
            m_DeletedLookup.Update(this);

            m_TerminalizationQueue.Drain(m_Outcomes);
            if (m_Outcomes.Count == 0)
                return;

            m_Outcomes.Sort(static (left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                if (priority != 0) return priority;
                int index = left.Entity.Index.CompareTo(right.Entity.Index);
                if (index != 0) return index;
                return left.Entity.Version.CompareTo(right.Entity.Version);
            });

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            EntityCommandBuffer EnsureEcb()
            {
                if (!ecbCreated)
                {
                    ecb = m_ThreatLifecycleBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }
                return ecb;
            }

            int accepted = 0;
            int hits = 0;
            int droneHits = 0;
            int ballisticHits = 0;
            int crashed = 0;
            int intercepts = 0;
            int commands = 0;

            for (int i = 0; i < m_Outcomes.Count; i++)
            {
                var outcome = m_Outcomes[i];
                if (!CanApply(outcome.Entity))
                    continue;

                if (!m_ThreatLifecycleDedup.TryQueueDeleted(outcome.Entity))
                    continue;

                QueueThreatRenderEntityDeletion(EnsureEcb(), outcome.Entity, ref commands);

                if (outcome.CreatesDebris)
                    QueueRenderlessDebrisTimer(EnsureEcb(), in outcome, ref commands);

                if (outcome.EmitsImmediateImpact)
                {
                    EnqueueImmediateImpact(in outcome);
                    hits++;
                    // Drone/ballistic split for balance telemetry — outcome.IsBallistic is the same
                    // flag the impact event below already carries; this only books the slice.
                    if (outcome.IsBallistic)
                        ballisticHits++;
                    else
                        droneHits++;
                    EventBus?.SafePublish(new ThreatImpactEvent(outcome.EventPosition, outcome.IsBallistic), nameof(ThreatTerminalizationSystem));
                }

                if (outcome.IsAcceptedIntercept)
                {
                    intercepts++;
                    m_AudioService.PlayInterceptSound(outcome.Position);
                    m_VanillaVfx?.RequestExplosion(outcome.Position, ExplosionType.Intercept);
                    EventBus.SafePublish(new ThreatInterceptEvent(outcome.Position, outcome.IsBallistic), nameof(ThreatTerminalizationSystem));
                }

                if (outcome.IsCrashedArrival)
                    crashed++;

                accepted++;
            }

            if (commands > 0)
            {
                AddToEcbCount(commands);
                m_ThreatLifecycleBarrier.AddJobHandleForProducer(Dependency);
            }

            AddOutcomeStats(hits, droneHits, ballisticHits, crashed);
            AddInterceptStats(intercepts);

            if (accepted > 0 && Log.IsDebugEnabled)
                Log.Debug($"Applied {accepted} terminal outcome(s): hits={hits}, crashed={crashed}, intercepts={intercepts}");
        }

        protected override void OnStopRunning()
        {
            m_TerminalizationQueue?.Clear();
            m_Outcomes.Clear();
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            if (m_ProducerImpacts.IsCreated) m_ProducerImpacts.Dispose();
            if (m_ConsumerImpacts.IsCreated) m_ConsumerImpacts.Dispose();
            base.OnDestroy();
        }

        public bool TryTransferPendingImpacts(out NativeList<ThreatImpactData> impacts)
        {
            if (m_ConsumerOwnsImpacts)
            {
                impacts = m_ConsumerImpacts;
                return impacts.IsCreated && impacts.Length > 0;
            }

            if (!HasPendingImpacts)
            {
                impacts = default;
                return false;
            }

            (m_ConsumerImpacts, m_ProducerImpacts) = (m_ProducerImpacts, m_ConsumerImpacts);
            m_ProducerImpacts.Clear();
            m_ConsumerOwnsImpacts = true;
            impacts = m_ConsumerImpacts;
            return impacts.Length > 0;
        }

        public void CompletePendingImpactTransfer()
        {
            if (!m_ConsumerOwnsImpacts)
                return;

            m_ConsumerImpacts.Clear();
            m_ConsumerOwnsImpacts = false;
        }

        private void ResolveServices()
        {
            m_ThreatLifecycleDedup ??= ServiceRegistry.Instance.Require<IThreatLifecycleDedup>();
            m_TerminalizationQueue ??= ServiceRegistry.Instance.Require<IThreatTerminalizationSink>();
            m_AudioService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatAudioService.Instance);
            m_VanillaVfx ??= World.GetExistingSystemManaged<VanillaVfxSystem>();
        }

        private bool CanApply(Entity entity)
        {
            if (!m_StorageInfoLookup.Exists(entity))
                return false;
            if (m_DeletedLookup.HasComponent(entity))
                return false;
            if (m_PendingDestructionLookup.HasComponent(entity)
                && m_PendingDestructionLookup.IsComponentEnabled(entity))
                return false;
            return m_ActiveThreatLookup.HasComponent(entity)
                && m_ActiveThreatLookup.IsComponentEnabled(entity);
        }

        private void QueueThreatRenderEntityDeletion(EntityCommandBuffer ecb, Entity entity, ref int commands)
        {
            ecb.SetComponentEnabled<ActiveThreat>(entity, false);
            commands++;

            if (m_PendingDestructionLookup.HasComponent(entity)
                && !m_PendingDestructionLookup.IsComponentEnabled(entity))
            {
                ecb.SetComponentEnabled<PendingDestruction>(entity, true);
                commands++;
            }

            // Render-safe deletion: do NOT add Deleted here. Adding it from GameSimulation
            // (LateUpdate, end of frame) migrates the drone's render chunk out of phase with
            // the vanilla render batch pass → zeroed render chunk-cache crash. Instead flip the
            // enableable signal (not a structural change, no chunk migration) and let
            // ThreatDeletionApplySystem do the AddComponent<Deleted> from Modification4, the
            // phase the render pipeline expects (mirror of vanilla IgniteSystem). The
            // ActiveThreat/PendingDestruction enable-bits above stay here behind the
            // render-handle-completing ThreatLifecycleBarrier for render safety.
            if (!m_DeletedLookup.HasComponent(entity))
            {
                ecb.SetComponentEnabled<PendingThreatDeletion>(entity, true);
                commands++;
            }
        }

        private static void QueueRenderlessDebrisTimer(EntityCommandBuffer ecb, in ThreatTerminalOutcome outcome, ref int commands)
        {
            var debrisEntity = ecb.CreateEntity();
            ecb.AddComponent(debrisEntity, FallingDebris.FromThreat(
                outcome.Position,
                outcome.DebrisFallTime,
                outcome.ThreatGeneration));
            commands += 2;
        }

        private void EnqueueImmediateImpact(in ThreatTerminalOutcome outcome)
        {
            m_ProducerImpacts.Add(new ThreatImpactData
            {
                Position = outcome.Position,
                Type = outcome.IsBallistic ? ImpactType.Ballistic : ImpactType.DirectHit,
                Severity = outcome.DamageSeverity,
                Radius = outcome.ImpactRadius,
                ThreatGeneration = outcome.ThreatGeneration
            });
        }

        private void AddOutcomeStats(int hits, int droneHits, int ballisticHits, int crashed)
        {
            if (hits == 0 && crashed == 0)
                return;

            if (!TryGetOutcomeStatsForCurrentWave(out var stats))
                return;

#pragma warning disable CIVIC069
            stats.ValueRW.HitsCount += hits;
            stats.ValueRW.DroneHitsCount += droneHits;
            stats.ValueRW.BallisticHitsCount += ballisticHits;
            stats.ValueRW.CrashedCount += crashed;
#pragma warning restore CIVIC069
            Log.Info($" Wave #{stats.ValueRO.WaveNumber}: +{hits} hits (drone {droneHits}/ballistic {ballisticHits}), +{crashed} crashed (total: {stats.ValueRO.HitsCount}/{stats.ValueRO.CrashedCount})");
        }

        private bool TryGetOutcomeStatsForCurrentWave(out RefRW<ThreatOutcomeStatsSingleton> stats)
        {
            if (!SystemAPI.TryGetSingletonRW<ThreatOutcomeStatsSingleton>(out stats))
            {
#pragma warning disable CIVIC437
                ThreatOutcomeStatsSingleton.EnsureExists(EntityManager);
#pragma warning restore CIVIC437
                if (!SystemAPI.TryGetSingletonRW<ThreatOutcomeStatsSingleton>(out stats))
                    return false;
            }

            int currentWave = 0;
            if (SystemAPI.TryGetSingleton<WaveStateSingleton>(out var waveState))
                currentWave = waveState.WaveNumber;

            if (stats.ValueRO.WaveNumber != currentWave)
            {
                stats.ValueRW = new ThreatOutcomeStatsSingleton
                {
                    WaveNumber = currentWave,
                    HitsCount = 0,
                    DroneHitsCount = 0,
                    BallisticHitsCount = 0,
                    CrashedCount = 0
                };
            }

            return true;
        }

        private void AddInterceptStats(int intercepts)
        {
            if (intercepts <= 0)
                return;

            // InterceptedCount is incremented at DECISION time in InterceptProcessingSystem (wave-accurate,
            // save-stable) — NOT here at terminalization, which for deferred Patriot intercepts lands
            // ~missile-flight later and could cross a wave reset / be lost on a mid-coast save. This logs
            // how many visually terminalized this drain, for diagnostics only.
            if (SystemAPI.TryGetSingleton<InterceptStatsSingleton>(out var stats))
                Log.Info($"Terminalized {intercepts} intercept(s) this drain; wave total={stats.InterceptedCount}");
        }
    }
}
