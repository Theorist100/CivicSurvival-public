using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// Pause-safe post-load reinitialization of threat render-state (C1).
    ///
    /// Threats now PERSIST across save/load (Shahed/Ballistic/ThreatPosition are ISerializable).
    /// But a restored drone comes back with empty render buffers (MeshColor/TransformFrame are
    /// IEmptySerializable) and WITHOUT its lifecycle tags / PreCulling flags (ActiveThreat,
    /// PendingDestruction, Created, Updated have no serializer, so the serializer strips them
    /// from the archetype — decompile-proven, Colossal.Core ComponentSerializerLibrary). A
    /// length-0 MeshColor buffer crashes vanilla BatchDataSystem (OOB read by m_MeshIndex in
    /// the render Burst job), and missing ActiveThreat drops the drone out of the wave's
    /// active-threat query so the resume logic would see zero live threats.
    ///
    /// This one-shot rebuilds, on the first ModificationEnd of a loaded session (pause-safe,
    /// ticks under pausedAfterLoading, ordered before PreCulling within the same frame), the
    /// exact state ThreatSpawnSystem sets at spawn:
    /// - re-add ActiveThreat (enabled) + PendingDestruction (disabled),
    /// - re-add Created + Updated so PreCulling/BatchInstanceSystem rebuild the MeshBatch,
    /// - prefill MeshColor (one white entry per prefab submesh — never length 0),
    /// - prefill TransformFrame (4 OIS history slots from the restored ThreatPosition/Velocity),
    /// - re-stamp ThreatGeneration = ThreatGenerationClock.Current (the load boundary already
    ///   advanced the clock in ScenarioStateMachine.Deserialize, so a restored drone is
    ///   adopted into the loaded world instead of dropped as a stale zombie).
    ///
    /// A threat that was already intercepted or arrived at save time is NOT resurrected: it is
    /// destroyed here (same as a tracer). Rehydrating arrived threats would either replay
    /// ballistic impact damage or leave an arrived Shahed counted as live forever.
    /// "Was intercepted" is read from the persisted ShahedCombatState/BallisticInterceptState
    /// (the first durable write at intercept success) — no separate flag on Shahed/Ballistic.
    ///
    /// One-shot: armed on a gameplay load, fires on the first ModificationEnd tick, then
    /// disables itself. Never runs during normal gameplay, so live in-flight threats are never
    /// touched.
    /// </summary>
    [ActIndependent]
    [ReentrantOneShot("Runs once per load: armed in OnGameLoaded, fires on the first ModificationEnd tick, disables itself in OnUpdateImpl. Re-arms on the next load via OnGameLoaded.")]
    public partial class ThreatLoadRenderReinitSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatLoadRenderReinit");

        // ReadWrite: ThreatGeneration is written back via SetComponentData during reinit.
        private EntityQuery m_ShahedQuery;
        private EntityQuery m_BallisticQuery;
        private EntityQuery m_ResumeStateQuery;

        [System.NonSerialized] private bool m_ReinitPending;
        [System.NonSerialized] private ThreatGenerationClock m_GenerationClock = null!;

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ShahedQuery = GetEntityQuery(ComponentType.ReadWrite<Shahed>());
            m_BallisticQuery = GetEntityQuery(ComponentType.ReadWrite<Ballistic>());
            m_ResumeStateQuery = GetEntityQuery(ComponentType.ReadWrite<ThreatLoadResumeState>());

            Enabled = false;
            Log.Info("Created (disabled until game load)");
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            // Only gameplay loads carry restored threats. Editor/asset/menu contexts never
            // spawn them; arming there would be a harmless no-op but the gate is kept explicit.
            if (serializationContext.purpose != Purpose.LoadGame
                && serializationContext.purpose != Purpose.NewGame)
                return;

            ThreatLoadResumeState.EnsureExists(EntityManager);
            if (m_ResumeStateQuery.TryGetSingletonEntity<ThreatLoadResumeState>(out var resumeEntity))
            {
                var previous = EntityManager.GetComponentData<ThreatLoadResumeState>(resumeEntity);
                var next = ThreatLoadResumeState.Default;
                if (previous.RestorePolicy == ThreatLoadRestorePolicy.DiscardRestoredThreats)
                    next.RestorePolicy = ThreatLoadRestorePolicy.DiscardRestoredThreats;
                EntityManager.SetComponentData(resumeEntity, next);
            }
            Arm();
        }

        /// <summary>
        /// Arm the one-shot for the next ModificationEnd tick. The Enabled write lives in this
        /// helper (not inline in OnGameLoaded) deliberately: the pending flag is set fresh on
        /// every load, so there is no cross-enable-cycle state for an OnStartRunning reset to
        /// clear — and an OnStartRunning override would fire before the first OnUpdate and clear
        /// the arm before the reinit runs. Mirrors the prior ThreatLoadPurgeSystem.ArmPurge.
        /// </summary>
        private void Arm()
        {
            m_ReinitPending = true;
            Enabled = true;
        }

        [CompletesDependency("One-shot post-load reinit/purge of restored threats; ToEntityArray/structural sync runs once per load, not per frame")]
        protected override void OnUpdateImpl()
        {
            if (!m_ReinitPending)
            {
                Enabled = false;
                return;
            }

            m_ReinitPending = false;
            Enabled = false;

            // Mark this one-shot post-load render reinit so a crash/hang here is attributable (it runs
            // outside the sim-tick window, otherwise recovers as the blind Unknown). Cleared on both
            // exit paths below; one disk write in / out (one-shot, not per-frame).
            Core.Diagnostics.NativeCrashBreadcrumb.Mark(Core.Diagnostics.NativeCrashMarkers.LoadRenderReinit);

            // Clear cross-frame threat dedup state inherited from the previous session before
            // touching entities (mirrors the previous purge order).
            var dedup = ServiceRegistry.TryGet<IThreatLifecycleDedup>();
            if (dedup != null)
                dedup.Clear();

            m_GenerationClock ??= ServiceRegistry.Instance.Require<ThreatGenerationClock>();
            int generation = m_GenerationClock.Current;

            if (ShouldDiscardRestoredThreats())
            {
                int purged = PurgeAllRestoredThreats();
                PublishResumeState(0, 0, purged, ThreatLoadRestorePolicy.DiscardRestoredThreats);
                Log.Warn($"Discarded {purged} restored threat(s) because WaveExecutor restore failed or mismatched");
                Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(Core.Diagnostics.NativeCrashMarkers.LoadRenderReinit);
                return;
            }

            double progressResetTime = SystemAPI.Time.ElapsedTime;
            int shahedReinit = ReinitShaheds(generation, progressResetTime, out int shahedPurged);
            int ballisticReinit = ReinitBallistics(generation, progressResetTime, out int ballisticPurged);

            PublishResumeState(shahedReinit, ballisticReinit, shahedPurged + ballisticPurged, ThreatLoadRestorePolicy.Resume);

            // DESIGN — this system deliberately does NOT write ThreatStatsSingleton here. That
            // singleton is owned by ThreatTargetSystem ([SingletonOwner]); seeding it from this
            // system would break the single-writer invariant. A direct EntityManager write also
            // slips past the single-writer analyzer (it only tracks the SetSingleton APIs), so a
            // clean build would NOT prove it safe. The owner reseeds it in
            // ThreatTargetSystem.ValidateAfterLoad. The only visible effect is cosmetic: on a
            // paused-after-load start the threat-count UI reads 0 until the first unpaused
            // GameSimulation tick. This is an accepted trade-off, not a missing seed — do not
            // re-add a stats write here.
            if (shahedReinit > 0 || ballisticReinit > 0)
                Log.Info($"Reinit {shahedReinit} Shaheds, {ballisticReinit} Ballistics restored from save (render-state + lifecycle tags re-applied before first PreCulling)");

            Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(Core.Diagnostics.NativeCrashMarkers.LoadRenderReinit);
        }

        // EntityManager access below is intentional and not a per-frame hot path: this is a
        // one-shot post-load reinit that must apply synchronous structural changes (AddComponent /
        // DestroyEntity) and the interleaved buffer/data writes BEFORE the same frame's PreCulling.
        // A cached ComponentLookup cannot be used because each AddComponent invalidates it.
        // ToEntityArray/structural sync runs once per load, not per frame.
#pragma warning disable CIVIC051, CIVIC218
        private int ReinitShaheds(int generation, double progressResetTime, out int purged)
        {
            purged = 0;
            if (m_ShahedQuery.IsEmptyIgnoreFilter)
                return 0;

            var entities = m_ShahedQuery.ToEntityArray(Allocator.Temp);
            int reinit = 0;
            try
            {
                foreach (var entity in entities)
                {
                    var shahed = EntityManager.GetComponentData<Shahed>(entity);
                    bool intercepted = EntityManager.HasComponent<ShahedCombatState>(entity)
                        && EntityManager.GetComponentData<ShahedCombatState>(entity).IsIntercepted;

                    // DESIGN — destroying a restored threat here is INTENTIONAL, not a leak/double-handling.
                    // It applies ONLY to threats that were already terminal at save time (shot down →
                    // ShahedCombatState.IsIntercepted, or already arrived & impacted → Shahed.IsArrived).
                    // We cannot avoid writing them to the save: vanilla SerializerSystem persists EVERY
                    // entity with PrefabRef (Any(PrefabRef) query), so a terminal drone round-trips
                    // regardless. On load it MUST be cleared — leaving it crashes the renderer (empty
                    // MeshColor), and "rehydrating it as live" is a real bug, not an optimization:
                    //   - a re-lived arrived Ballistic re-enters the TMS/TAS arrival pipeline (its dedup
                    //     set is runtime-only, empty after load) → applies impact damage a SECOND time;
                    //   - a re-lived arrived Shahed is never re-processed by movement nor destroyed →
                    //     a zombie that keeps the active-threat count > 0 → the wave never finalizes.
                    // LIVE in-flight threats (the common case) fall through and are reinitialized + resumed.
                    if (intercepted || shahed.IsArrived)
                    {
                        DestroyRestored(entity);
                        purged++;
                        continue;
                    }

                    ReinitRender(entity, addIdentifiedTarget: true);
                    ResetThreatFlightProgress(entity, progressResetTime);
                    shahed.ThreatGeneration = generation;
                    EntityManager.SetComponentData(entity, shahed);
                    reinit++;
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (purged > 0)
                Log.Info($"Purged {purged} already intercepted/arrived Shaheds restored from save (not resurrected)");
            return reinit;
        }

        private int ReinitBallistics(int generation, double progressResetTime, out int purged)
        {
            purged = 0;
            if (m_BallisticQuery.IsEmptyIgnoreFilter)
                return 0;

            var entities = m_BallisticQuery.ToEntityArray(Allocator.Temp);
            int reinit = 0;
            try
            {
                foreach (var entity in entities)
                {
                    var ballistic = EntityManager.GetComponentData<Ballistic>(entity);
                    bool intercepted = EntityManager.HasComponent<BallisticInterceptState>(entity)
                        && EntityManager.GetComponentData<BallisticInterceptState>(entity).IsIntercepted;

                    // DESIGN — see ReinitShaheds: destroying a terminal restored threat is INTENTIONAL.
                    // For Ballistic specifically, re-living one with persisted IsArrived==true re-runs the
                    // arrival pipeline and applies impact damage twice. Terminal threats are purged, not
                    // resurrected; only live in-flight missiles fall through to reinit + resume.
                    if (intercepted || ballistic.IsArrived)
                    {
                        DestroyRestored(entity);
                        purged++;
                        continue;
                    }

                    ReinitRender(entity, addIdentifiedTarget: false);
                    ResetThreatFlightProgress(entity, progressResetTime);
                    ballistic.ThreatGeneration = generation;
                    EntityManager.SetComponentData(entity, ballistic);
                    reinit++;
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (purged > 0)
                Log.Info($"Purged {purged} already intercepted/arrived Ballistics restored from save (not resurrected)");
            return reinit;
        }

        // Synchronous structural reinit: an ECB would defer to end-of-frame, after this frame's
        // PreCulling has already read the empty MeshColor buffers — exactly the crash being
        // prevented.
#pragma warning disable CIVIC006, CIVIC208
        private void ReinitRender(Entity entity, bool addIdentifiedTarget)
        {
            // Lifecycle tags: ActiveThreat (enabled) + PendingDestruction (disabled). Both are
            // enableable tags with no serializer → normally absent on the restored entity
            // (stripped on save). Guard each add so a partially restored/debug entity cannot
            // crash reinit by adding a component it already has.
#pragma warning disable CIVIC485 // Structural existence guard before re-adding stripped enableable tags; enabled state is set explicitly below.
            if (!EntityManager.HasComponent<ActiveThreat>(entity))
                EntityManager.AddComponent<ActiveThreat>(entity);
            EntityManager.SetComponentEnabled<ActiveThreat>(entity, true);
            if (!EntityManager.HasComponent<PendingDestruction>(entity))
                EntityManager.AddComponent<PendingDestruction>(entity);
            // PendingThreatDeletion: render-safe deletion signal, also enableable with no
            // serializer → stripped on save. Re-add disabled so the producer can flip it on a
            // re-terminalized restored drone (SetComponentEnabled needs the component present).
            if (!EntityManager.HasComponent<PendingThreatDeletion>(entity))
                EntityManager.AddComponent<PendingThreatDeletion>(entity);
            // Faction tag: PlayerOutboundThreat is an enableable component with no serializer, so
            // it is stripped on save/load like the lifecycle tags above. For an inbound wave threat
            // that is correct (absent/disabled bit == EnemyInbound, the pre-faction default). But a
            // restored PLAYER counter-strike would also load with the bit gone — reverting it to an
            // inbound threat that the player's own AA shoots down (CollectThreatJob's
            // Exclude<PlayerOutboundThreat> no longer excludes it once the bit is absent) and that
            // terminalizes into the city. The OutboundStrikePayload it carries IS serialized, so its
            // IsOutbound fact survives the round-trip — re-add (disabled by default) and re-enable the
            // tag here, in ModificationEnd, before the first GameSimulation AA scan after load (the
            // AA orchestrator runs in GameSimulation, which this ModificationEnd one-shot precedes
            // within the same loaded frame), so the restored projectile keeps its side and lands its
            // axis effect on arrival (OutboundStrikePayload still drives ThreatArrivalSystem).
            bool isOutbound = EntityManager.HasComponent<OutboundStrikePayload>(entity)
                && EntityManager.GetComponentData<OutboundStrikePayload>(entity).IsOutbound;
            if (!EntityManager.HasComponent<PlayerOutboundThreat>(entity))
                EntityManager.AddComponent<PlayerOutboundThreat>(entity);
#pragma warning restore CIVIC485
            EntityManager.SetComponentEnabled<PendingDestruction>(entity, false);
            EntityManager.SetComponentEnabled<PendingThreatDeletion>(entity, false);
            EntityManager.SetComponentEnabled<PlayerOutboundThreat>(entity, isOutbound);
            if (addIdentifiedTarget && !EntityManager.HasComponent<IdentifiedTarget>(entity))
                EntityManager.AddComponent<IdentifiedTarget>(entity);

            // PreCulling flags: PreCullingJob only flags an entity for MeshBatch rebuild when it
            // carries Created/Updated. Both are stripped on save → re-add so BatchInstanceSystem
            // rebuilds the restored drone's MeshBatch this frame.
            if (!EntityManager.HasComponent<Created>(entity))
                EntityManager.AddComponent<Created>(entity);
            if (!EntityManager.HasComponent<Updated>(entity))
                EntityManager.AddComponent<Updated>(entity);

            // MeshColor bridge: BatchDataSystem reads MeshColor[m_MeshIndex] without bounds check.
            // A length-0 buffer = OOB = native crash. Prefill one white entry per prefab submesh
            // (>= 1) before PreCulling; MeshColorSystem's load path resizes/recolors it later the
            // same frame. Buffer handles are fetched AFTER all structural changes above. The
            // archetype always carries the MeshColor buffer (IEmptySerializable → present, empty).
            if (EntityManager.HasBuffer<MeshColor>(entity))
            {
                int submeshCount = GetPrefabSubMeshCount(entity);
                var meshColors = EntityManager.GetBuffer<MeshColor>(entity);
                meshColors.Clear();
                for (int i = 0; i < submeshCount; i++)
                {
                    meshColors.Add(new MeshColor
                    {
                        m_ColorSet = new ColorSet
                        {
                            m_Channel0 = UnityEngine.Color.white,
                            m_Channel1 = UnityEngine.Color.white,
                            m_Channel2 = UnityEngine.Color.white
                        }
                    });
                }
            }

            // TransformFrame: 4 OIS history slots offset backward by velocity from the restored
            // position, so OIS Bezier interpolation has consistent history from frame 0 (empty
            // slots → backward jump). Mirrors ThreatSpawnSystem's preseed.
            if (EntityManager.HasBuffer<TransformFrame>(entity))
            {
                var threatPos = EntityManager.GetComponentData<ThreatPosition>(entity);
                float3 position = threatPos.Position;
                float3 velocity = threatPos.Velocity;
                quaternion rotation = threatPos.Rotation;
                var frames = EntityManager.GetBuffer<TransformFrame>(entity);
                frames.Clear();
                for (int i = 0; i < 4; i++)
                {
                    float timeBack = (3 - i) * FIXED_TIME_STEP;
                    frames.Add(new TransformFrame
                    {
                        m_Position = position - velocity * timeBack,
                        m_Velocity = velocity,
                        m_Rotation = rotation
                    });
                }
            }
        }

        private void DestroyRestored(Entity entity)
        {
            EntityManager.DestroyEntity(entity);
        }

        private void ResetThreatFlightProgress(Entity entity, double progressResetTime)
        {
            var progress = new ThreatFlightProgress
            {
                MinDistanceToTarget = float.MaxValue,
                MinDistanceTime = progressResetTime
            };

            if (EntityManager.HasComponent<ThreatFlightProgress>(entity))
                EntityManager.SetComponentData(entity, progress);
            else
                EntityManager.AddComponentData(entity, progress);
        }
#pragma warning restore CIVIC006, CIVIC208

        private bool ShouldDiscardRestoredThreats()
        {
            return m_ResumeStateQuery.TryGetSingleton<ThreatLoadResumeState>(out var state)
                && state.RestorePolicy == ThreatLoadRestorePolicy.DiscardRestoredThreats
                || ThreatLoadRestorePolicyLatch.ConsumeDiscardRestoredThreats();
        }

        private int PurgeAllRestoredThreats()
        {
            int purged = 0;
            if (!m_ShahedQuery.IsEmptyIgnoreFilter)
            {
                var shaheds = m_ShahedQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    foreach (var entity in shaheds)
                    {
                        DestroyRestored(entity);
                        purged++;
                    }
                }
                finally
                {
                    shaheds.Dispose();
                }
            }

            if (!m_BallisticQuery.IsEmptyIgnoreFilter)
            {
                var ballistics = m_BallisticQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    foreach (var entity in ballistics)
                    {
                        DestroyRestored(entity);
                        purged++;
                    }
                }
                finally
                {
                    ballistics.Dispose();
                }
            }

            return purged;
        }

        private void PublishResumeState(int liveShaheds, int liveBallistics, int purgedTerminalThreats, ThreatLoadRestorePolicy policy)
        {
            if (!m_ResumeStateQuery.TryGetSingletonEntity<ThreatLoadResumeState>(out var resumeEntity))
            {
                Log.Error("ThreatLoadResumeState missing during load reinit; resume decision will use durable fallback");
                return;
            }

            EntityManager.SetComponentData(resumeEntity, new ThreatLoadResumeState
            {
                LiveShaheds = liveShaheds,
                LiveBallistics = liveBallistics,
                PurgedTerminalThreats = purgedTerminalThreats,
                ReinitCompleted = true,
                RestorePolicy = policy
            });
        }

        /// <summary>
        /// SubMesh count of the drone's prefab (>= 1). The MeshColor bridge needs at least as
        /// many entries as the highest m_MeshIndex BatchDataSystem will read this frame. Falls
        /// back to 1 (the proven single-submesh AttackDrone case) when the prefab carries no
        /// SubMesh buffer.
        /// </summary>
        private int GetPrefabSubMeshCount(Entity entity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity))
                return 1;
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !EntityManager.HasBuffer<SubMesh>(prefab))
                return 1;
            return math.max(1, EntityManager.GetBuffer<SubMesh>(prefab, true).Length);
        }
#pragma warning restore CIVIC051, CIVIC218

        // OIS fixed-step used for the 4-slot TransformFrame preseed (mirrors ThreatSpawnSystem).
        private const float FIXED_TIME_STEP = 4f / 15f;
    }
}
