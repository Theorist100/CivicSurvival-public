using Colossal.Serialization.Entities;
using System.Collections.Generic;
using Game.Effects;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Injects VFX and SFX into vanilla EnabledEffectData pipeline (Approach 7c).
    ///
    /// VFX: explosion particles via VFXSystem (IsVFX flag).
    /// SFX: one-shot audio via AudioManager Temp queue — replaces EntityManager.Instantiate.
    ///
    /// Architecture: Anchor-entity owned entries in EnabledEffectData, plus
    /// owner-attached entries (TryAttachEffect) whose m_Owner is a live entity and
    /// whose lifecycle is fully vanilla-owned (no expiry/Reset bookkeeping here).
    /// Each anchor effect is added ONCE and removed ONCE (not re-injected every frame).
    /// CompleteEnabledSystem maintains index consistency via the anchor entity's
    /// EnabledEffect buffer — on RemoveAtSwapBack, it updates m_EnabledIndex
    /// in the buffer and sends MoveIndex to VFXSystem.
    ///
    /// Key fix from 7a: m_Owner = anchor entity (not Entity.Null).
    /// Entity.Null caused CompleteEnabledSystem to crash/corrupt on swap-back
    /// because it couldn't update the non-existent owner's EnabledEffect buffer.
    ///
    /// Zero cost when no active effects — early return before any sync point.
    ///
    /// Coordinator only — actual work delegated to:
    /// <see cref="ExplosionVfxChannel"/>, <see cref="OneShotSfxChannel"/>,
    /// <see cref="VanillaEffectAnchor"/>, <see cref="VanillaVfxDiagnostics"/>.
    ///
    /// P11 ordering exception: the old After(EffectControlSystem) edge is covered
    /// by phase order (EffectControlSystem runs in PreCulling, this system in
    /// GameSimulation; vanilla UpdateSystem sorts by phase before addIndex). The
    /// old Before(VFXSystem) edge crosses into Rendering and cannot be represented
    /// as a same-phase RefMap edge; registration explicitly anchors this system
    /// after EffectCacheSystem, while Rendering-phase VFX consumes later by phase.
    /// </summary>
    [ActIndependent]
    public partial class VanillaVfxSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaVfxSystem");

        // Vanilla system references
        private EffectControlSystem m_EffectControlSystem = null!;
        private VFXSystem m_VFXSystem = null!;
        private EffectCacheSystem m_EffectCache = null!;
        private Game.Audio.AudioManager m_AudioManager = null!;

        private BufferLookup<EnabledEffect> m_EnabledEffectLookup;
        private BufferLookup<Game.Prefabs.Effect> m_DummyEffectLookup;

        private readonly VanillaEffectAnchor m_Anchor = new();
        private readonly ExplosionVfxChannel m_Vfx = new();
        private readonly OneShotSfxChannel m_Sfx = new();
        private readonly VanillaVfxDiagnostics m_Diag = new();

        // Shared VFX effect id sequence for anchor-owned EnabledEffectData entries.
        private int m_NextEffectId;
        private bool m_Initialized;
        private int m_InitFailureFrames;
        private const int MAX_INIT_FAILURE_FRAMES = 300;

        private readonly struct PendingSfxRequest
        {
            public PendingSfxRequest(string name, float3 position)
            {
                Name = name;
                Position = position;
            }

            public string Name { get; }
            public float3 Position { get; }
        }

        private readonly List<PendingSfxRequest> m_PendingSfxBeforeInit = new();

        // Owner-attach requests collected in GameSimulation by the exhaust controllers
        // (InterceptorExhaustSystem, ThreatSpawnSystem.EnsureBallisticExhaustAttached) and
        // drained ONCE per frame by VanillaVfxLateAttachSystem in CompleteRendering. The drain
        // (GetEnabledData(false).Complete()) is moved out of GameSimulation deliberately — see
        // the PERF-LOCK on FlushOwnerAttachQueue. Persistent across frames (cleared on every
        // flush), allocated in OnCreate.
        private NativeList<VfxAttachRequest> m_OwnerAttachQueue;

        #region Public API

        /// <summary>
        /// Queue an explosion VFX at world position.
        /// Processed on next OnUpdate (same or next frame).
        /// </summary>
        /// <remarks>Main thread only. Do not call from worker jobs.</remarks>
        public void RequestExplosion(float3 position, ExplosionType type)
        {
            if (!m_Initialized)
            {
                Log.Warn($"[VFX:DIAG] RequestExplosion queued before initialization: type={type} pos=({position.x:F0},{position.y:F0},{position.z:F0})");
                m_Vfx.Enqueue(position, type);
                return;
            }
            if (Log.IsDebugEnabled)
            {
                Log.Debug($"[VFX:DIAG] RequestExplosion queued: type={type} pos=({position.x:F0},{position.y:F0},{position.z:F0}) " +
                         $"pendingBefore={m_Vfx.PendingCount} activeCount={m_Vfx.ActiveCount} frame={UnityEngine.Time.frameCount}");
            }
            m_Vfx.Enqueue(position, type);
        }

        /// <summary>
        /// Queue a one-shot SFX at world position.
        /// Sent through AudioManager's Temp path; AudioManager resolves AudioSourceData
        /// from the requested SFX prefab and applies vanilla distance culling.
        /// Zero EntityManager.Instantiate, zero main-thread structural changes.
        /// </summary>
        /// <remarks>Main thread only. Do not call from worker jobs.</remarks>
        public void RequestSfx(string sfxName, float3 position)
        {
            if (!m_Initialized)
            {
                if (m_PendingSfxBeforeInit.Count >= OneShotSfxChannel.MAX_PENDING_REQUESTS)
                {
                    Log.Warn($"[SFX] DROPPED pre-init request (pending full: {m_PendingSfxBeforeInit.Count}/{OneShotSfxChannel.MAX_PENDING_REQUESTS})");
                    return;
                }
                m_PendingSfxBeforeInit.Add(new PendingSfxRequest(sfxName, position));
                return;
            }
            if (m_Sfx.PendingCount >= OneShotSfxChannel.MAX_PENDING_REQUESTS)
            {
                Log.Warn($"[SFX] DROPPED request before prefab lookup (pending full: {m_Sfx.PendingCount}/{OneShotSfxChannel.MAX_PENDING_REQUESTS})");
                return;
            }
            if (!m_EffectCache.TryGetSfx(sfxName, out Entity prefab))
            {
                if (Log.IsDebugEnabled) Log.Debug($"[SFX] Audio effect not found or invalid: {sfxName}");
                return;
            }
            m_Sfx.Enqueue(prefab, position);
        }

        public bool IsReady => m_Initialized;

        /// <summary>
        /// Batch entry: ensure a live owner-attached EnabledEffectData record exists for
        /// every request. Each record's m_Owner is the live entity itself, so
        /// EffectTransformSystem moves the VFX with the owner's InterpolatedTransform
        /// every frame (DynamicTransform flag), reading pose offsets from element
        /// <see cref="VfxAttachRequest.EffectIndex"/> of the owner's prefab Effect buffer.
        ///
        /// Lifecycle is vanilla-owned: EffectControlSystem re-evaluations Disable the
        /// record (its prefab condition is not met — that is exactly why it is injected
        /// manually), Deleted owners get the standard Disable/cleanup, and
        /// PostDeserialize clears the whole m_EnabledData list on load. No expiry, no
        /// channel bookkeeping, no Reset participation here — callers re-attach via
        /// their own controller (see InterceptorExhaustSystem /
        /// ThreatSpawnSystem.EnsureBallisticExhaustAttached).
        ///
        /// Returns the number of requests that have a live record after the call. A
        /// request is skipped (not counted) when the owner/prefab wiring is not
        /// attachable (no PrefabRef, no prefab Effect element, element-prefab mismatch —
        /// e.g. a ballistic that fell back to the drone model).
        /// </summary>
        /// <remarks>
        /// Main thread only. The element/record prefab match is mandatory —
        /// EffectTransformSystem reads m_Effects[ownerPrefab][m_EffectIndex] unguarded,
        /// and a mismatch would get the record removed as WrongPrefab on the next
        /// re-evaluation.
        /// </remarks>
        public int AttachEffectBatch(NativeList<VfxAttachRequest> requests)
        {
            if (!m_Initialized || !requests.IsCreated || requests.Length == 0)
                return 0;

            // PERF-LOCK: one EnabledData drain per cycle — the N-interceptor / N-ballistic
            // exhaust batch must NOT re-Complete per entity (was ~800-1060ms under a wave,
            // VanillaProfiler 2026-06-24, drained the city-wide EffectControl graph once per
            // missile). The deps.Complete fence STAYS — it is hoisted out of the loop, not
            // removed: it still guards the structural Add/RemoveAt below against vanilla's
            // concurrent EnabledData writers (ResizeEnabledDataJob, EffectControlSystem
            // PreCulling). Drain once here, then all requests reuse the drained list.
            // PERF-LOCK: this drain runs from FlushOwnerAttachQueue in CompleteRendering (after the
            // frame's PreCulling+Rendering), where the engine's effect jobs are already done → the
            // Complete below is a noop. Do NOT call AttachEffectBatch from a GameSimulation system;
            // route through VanillaVfxSystem.EnqueueOwnerAttach instead (see FlushOwnerAttachQueue).
            var enabledData = m_EffectControlSystem.GetEnabledData(readOnly: false, out var deps);
            deps.Complete();

            int attached = 0;
            for (int r = 0; r < requests.Length; r++)
            {
                VfxAttachRequest req = requests[r];
                Entity owner = req.Owner;
                Entity effectPrefab = req.Prefab;
                int effectIndex = req.EffectIndex;

                if (effectPrefab == Entity.Null)
                    continue;

                // CIVIC051: batched controller — one drain above, ≤dozens of owners per
                // throttled cycle. Not a per-entity hot loop (each EntityManager read here
                // is metadata only; no per-owner sync point remains after the hoist).
#pragma warning disable CIVIC051
                if (!EntityManager.HasComponent<PrefabRef>(owner) || !EntityManager.HasBuffer<EnabledEffect>(owner))
                    continue;

                Entity ownerPrefab = EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab;
                if (!EntityManager.HasBuffer<Game.Prefabs.Effect>(ownerPrefab))
                    continue;

                var prefabEffects = EntityManager.GetBuffer<Game.Prefabs.Effect>(ownerPrefab, true);
                if (effectIndex < 0 || effectIndex >= prefabEffects.Length
                    || prefabEffects[effectIndex].m_Effect != effectPrefab)
                    continue;
                var element = prefabEffects[effectIndex];

                var ownerBuffer = EntityManager.GetBuffer<EnabledEffect>(owner);
#pragma warning restore CIVIC051

                bool live = false;
                for (int i = 0; i < ownerBuffer.Length; i++)
                {
                    var entry = ownerBuffer[i];
                    if (entry.m_EffectIndex != effectIndex)
                        continue;

                    if (entry.m_EnabledIndex >= 0 && entry.m_EnabledIndex < enabledData.Length)
                    {
                        var record = enabledData[entry.m_EnabledIndex];
                        if (record.m_Owner == owner && record.m_Prefab == effectPrefab
                            && (record.m_Flags & EnabledEffectFlags.IsEnabled) != 0)
                        {
                            live = true; // live record — nothing to do
                            break;
                        }
                    }

                    // Stale buffer entry (dead or foreign record behind the index). Drop it
                    // before re-adding: vanilla matches buffer entries by m_EffectIndex and
                    // a duplicate would shadow the fresh one in EnabledActionJob.
                    ownerBuffer.RemoveAt(i);
                    break;
                }

                if (live)
                {
                    attached++;
                    continue;
                }

                // Flag set mirrors vanilla EnabledActionJob.Enable for a moving VFX owner
                // (decompile EffectControlSystem.cs:783-807: IsVFX from VFXData, DynamicTransform
                // from InterpolatedTransform presence) minus the transient EnabledUpdated —
                // runtime-stable observation on these records is 0x411.
                // Re-read enabledData.Length each iteration — the list grows on every Add
                // across the batch, so the next record's index must reflect prior adds.
                int idx = enabledData.Length;
                enabledData.Add(new EnabledEffectData
                {
                    m_Owner = owner,
                    m_Prefab = effectPrefab,
                    m_EffectIndex = effectIndex,
                    m_Flags = EnabledEffectFlags.IsEnabled | EnabledEffectFlags.IsVFX | EnabledEffectFlags.DynamicTransform,
                    m_Position = req.Position,
                    m_Rotation = element.m_Rotation,
                    m_Scale = element.m_Scale,
                    m_Intensity = element.m_Intensity,
                    m_NextTime = 0f
                });

                m_VFXSystem.GetSourceUpdateData().Enqueue(new VFXUpdateInfo
                {
                    m_Type = VFXUpdateType.Add,
                    m_EnabledIndex = new int2(idx, 0)
                });

                ownerBuffer.Add(new EnabledEffect
                {
                    m_EffectIndex = effectIndex,
                    m_EnabledIndex = idx
                });

                if (Log.IsDebugEnabled)
                {
                    Log.Debug($"[VFX:ATTACH] owner={owner.Index}:{owner.Version} prefab={effectPrefab.Index} " +
                              $"effectIndex={effectIndex} enabledIdx={idx} flags=0x411 " +
                              $"pos=({req.Position.x:F0},{req.Position.y:F0},{req.Position.z:F0})");
                }

                attached++;
            }

            return attached;
        }

        /// <summary>
        /// Queue owner-attach requests for the deferred late-phase drain. Called by the exhaust
        /// controllers in GameSimulation (still render-frame-gated by the caller). The actual
        /// EnabledData drain + structural apply happens in <see cref="FlushOwnerAttachQueue"/>
        /// (CompleteRendering), not here — so GameSimulation does NOT pay the city-effect-graph
        /// wait. Copies the request values into the persistent queue; the caller's Temp list is
        /// theirs to dispose immediately.
        /// </summary>
        /// <remarks>Main thread only.</remarks>
        public void EnqueueOwnerAttach(NativeList<VfxAttachRequest> requests)
        {
            if (!m_OwnerAttachQueue.IsCreated || !requests.IsCreated || requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                m_OwnerAttachQueue.Add(requests[i]);
        }

        /// <summary>
        /// Drain every queued owner-attach request in one batch, then clear the queue. Invoked by
        /// <see cref="VanillaVfxLateAttachSystem"/> in CompleteRendering — AFTER this frame's
        /// PreCulling (EffectControlSystem schedules its graph) and Rendering (EffectTransformSystem
        /// reads/writes m_EnabledData), so by the time we GetEnabledData(false).Complete() the
        /// engine's effect jobs for the frame have already had the whole GameSimulation phase to
        /// finish → the fence is (near-)free instead of a real city-graph wait.
        ///
        /// PERF-LOCK: late-phase drain — the deps.Complete() inside AttachEffectBatch must run in
        /// CompleteRendering, NOT GameSimulation. EffectControlSystem schedules its city-wide graph
        /// in PreCulling (MainLoop) and EffectTransformSystem writes m_EnabledData in Rendering
        /// (MainLoop), both BEFORE GameSimulation (LateUpdate); draining in GameSimulation waited on
        /// that in-flight graph (~800-1060ms under a wave, VanillaProfiler 2026-06-25). Draining in
        /// CompleteRendering (after GameSimulation, same frame) makes Complete a noop. Do NOT move
        /// the drain back into a GameSimulation system.
        /// </summary>
        /// <remarks>
        /// Main thread only. Pause-gated by the caller (CompleteRendering ticks in pause; the queue
        /// is empty in pause anyway since the GameSimulation producers do not run, but the caller
        /// also guards selectedSpeed so a frozen frame never re-drains).
        /// </remarks>
        public void FlushOwnerAttachQueue()
        {
            if (!m_OwnerAttachQueue.IsCreated || m_OwnerAttachQueue.Length == 0)
                return;

            _ = AttachEffectBatch(m_OwnerAttachQueue);
            m_OwnerAttachQueue.Clear();
        }

        #endregion

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EffectControlSystem = World.GetOrCreateSystemManaged<EffectControlSystem>();
            m_VFXSystem = World.GetOrCreateSystemManaged<VFXSystem>();
            m_EffectCache = World.GetOrCreateSystemManaged<EffectCacheSystem>();
            m_AudioManager = World.GetOrCreateSystemManaged<Game.Audio.AudioManager>();

            m_EnabledEffectLookup = GetBufferLookup<EnabledEffect>(false);
            m_DummyEffectLookup = GetBufferLookup<Game.Prefabs.Effect>(false);

            m_OwnerAttachQueue = new NativeList<VfxAttachRequest>(64, Allocator.Persistent);

            Log.Info("Created (7c — anchor-entity owned)");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Vanilla GameManager.Save does NOT invoke onGamePreload (decompile
            // Game.SceneFlow/GameManager.cs:904-912 vs :1000-1007). Save-side
            // anchor destruction is owned by VanillaVfxSerializationBoundarySystem
            // running in SystemUpdatePhase.Serialize before BeginPrefabSerializationSystem.
            ResetForLoadBoundary("[VFX:7c] Cleared runtime anchor at load boundary");
        }

        /// <summary>
        /// Save boundary entry point invoked by <see cref="VanillaVfxSerializationBoundarySystem"/>
        /// in <c>SystemUpdatePhase.Serialize</c>, ordered before
        /// <c>Game.Serialization.BeginPrefabSerializationSystem</c>.
        ///
        /// Marks the runtime anchor and dummy as <c>Deleted</c> so:
        ///   - PrimaryPrefabReferencesSystem's PrefabRef query (excludes Deleted)
        ///     skips them during the PrefabReferences sub-phase that BPSS drives;
        ///   - SerializerSystem's main query (None: Deleted) skips them during
        ///     the Serialize phase.
        /// CleanUpSystem destroys Deleted entities in SystemUpdatePhase.Cleanup
        /// AFTER Serialize, so the next OnUpdateImpl observes the missing anchor
        /// and rebuilds via RecoverMissingAnchor (m_NextEffectId preserved).
        ///
        /// Idempotent: no-op if not initialized, or if anchor already gone.
        /// </summary>
        internal void PrepareForSaveSerialization()
        {
            if (!m_Initialized) return;
            if (!m_Anchor.Exists(EntityManager)) return;

            // CIVIC006: AddComponent<Deleted> is structural — but Serialize phase
            // runs once per save (not a hot path), so ECB is unnecessary.
            // CIVIC051: Exists() is bounded to one save event.
#pragma warning disable CIVIC006, CIVIC051
            var anchorEntity = m_Anchor.Entity;
            var dummyEntity  = m_Anchor.DummyPrefab;

            // Tear down anchor-owned explosion entries BEFORE the anchor is tagged Deleted.
            // CleanUpSystem destroys the anchor this same frame (Cleanup phase); any
            // anchor-owned EnabledEffectData entry left merely disabled becomes a
            // dangling-owner orphan that crashes vanilla CompleteEnabledSystem on a later
            // RemoveAtSwapBack — the save-during-attack crash. ForceExpireAllForSave marks
            // each entry Deleted so vanilla removes it via the owner-skipping branch.
            if (anchorEntity != Entity.Null && EntityManager.Exists(anchorEntity))
            {
                var enabledData = m_EffectControlSystem.GetEnabledData(readOnly: false, out var deps);
                deps.Complete();
                m_Vfx.ForceExpireAllForSave(anchorEntity, enabledData, m_VFXSystem.GetSourceUpdateData());
            }

            if (anchorEntity != Entity.Null
                && EntityManager.Exists(anchorEntity)
                && !EntityManager.HasComponent<Game.Common.Deleted>(anchorEntity))
            {
                EntityManager.AddComponent<Game.Common.Deleted>(anchorEntity);
            }
            if (dummyEntity != Entity.Null
                && EntityManager.Exists(dummyEntity)
                && !EntityManager.HasComponent<Game.Common.Deleted>(dummyEntity))
            {
                EntityManager.AddComponent<Game.Common.Deleted>(dummyEntity);
            }
#pragma warning restore CIVIC006, CIVIC051

            Log.Info($"[VFX:7c] Anchor tagged Deleted before save (anchor={anchorEntity.Index}:{anchorEntity.Version}, " +
                     $"dummy={dummyEntity.Index}:{dummyEntity.Version}). CleanUpSystem will destroy next Cleanup phase.");
        }

        protected override void OnUpdateImpl()
        {
            if (!m_Initialized)
            {
                TryInitialize();
                return;
            }

            // Defensive recovery if the runtime-only anchor was destroyed outside
            // an explicit save/load boundary.
            if (!m_Anchor.Exists(EntityManager))
            {
                RecoverMissingAnchor();
                return;
            }

            int currentFrame = UnityEngine.Time.frameCount;

            m_Diag.RunVerifyIfDue(this, currentFrame, m_Anchor.Entity,
                                  m_EffectControlSystem, m_VFXSystem, ref m_EnabledEffectLookup);

            m_Diag.TickFrame(currentFrame, m_Vfx.ActiveCount);

            // PERF-LOCK: skip-when-idle — no active effects and no pending requests means no
            // EnabledEffectData touch and no EffectControlSystem sync point. Removing this makes
            // every tick pay the sync even when there is nothing to render.
            if (m_Vfx.IsIdle && m_Sfx.IsIdle)
                return;

            // S22-H1 FIX: Use Time.time (pauses with game) instead of realtimeSinceStartup
            // (keeps ticking during pause/load screens, causing effects to expire while frozen).
            float now = UnityEngine.Time.time;

            // PERF-LOCK: skip-when-no-work — the EnabledEffectData sync (deps.Complete) costs
            // 3-16ms waiting for EffectControlSystem jobs. Only pay it when we have new requests
            // OR effects expiring. Removing this pays the sync on every active-effect frame.
            bool hasVfxWork = m_Vfx.PendingCount > 0 || m_Vfx.HasExpiring(now);
            bool hasSfxWork = m_Sfx.PendingCount > 0;
            if (!hasVfxWork && !hasSfxWork) return;

            if (hasSfxWork)
            {
                var sourceUpdateData = m_AudioManager.GetSourceUpdateData(out var audioDeps);
                var sfxRequests = m_Sfx.DrainPending(Allocator.TempJob);
                if (sfxRequests.IsCreated)
                {
                    // Canonical vanilla audio pattern (Game.Audio.SFXCullingSystem):
                    // enqueue temp sources from a job chained after audioDeps and
                    // register it via AddSourceUpdateWriter, so AudioManager's
                    // Cleanup-phase drain (AudioManager.OnUpdate → m_SourceUpdateWriter
                    // .Complete) waits for us — no main-thread audioDeps.Complete()
                    // stall. This system runs in GameSimulation, AudioManager in
                    // Cleanup, so the SFX still lands the same frame.
                    var sfxHandle = new AddTempSfxJob
                    {
                        Requests = sfxRequests,
                        SourceUpdateData = sourceUpdateData
                    }.Schedule(JobHandle.CombineDependencies(audioDeps, Dependency));

                    sfxRequests.Dispose(sfxHandle);
                    m_AudioManager.AddSourceUpdateWriter(sfxHandle);
                    Dependency = sfxHandle;
                }
            }

            if (!hasVfxWork) return;

            m_EnabledEffectLookup.Update(this);
            m_DummyEffectLookup.Update(this);

            // Sync point — only reached when we need to add or expire effects
            var enabledData = m_EffectControlSystem.GetEnabledData(readOnly: false, out var deps);
            deps.Complete();
            var vfxQueue = m_VFXSystem.GetSourceUpdateData();
            var anchorBuffer = m_Anchor.GetBuffer(m_EnabledEffectLookup);
            if (!m_DummyEffectLookup.HasBuffer(m_Anchor.DummyPrefab))
            {
                RecoverMissingAnchor();
                return;
            }
            var dummyEffectBuffer = m_DummyEffectLookup[m_Anchor.DummyPrefab];

            if (m_Vfx.PendingCount > 0 || m_Vfx.ActiveCount > 0)
                m_Diag.LogState("PRE", enabledData, anchorBuffer, m_Vfx.ActiveCount, m_Vfx.PendingCount);

            // CompleteEnabledSystem removes disabled entries in Cleanup, after this system's
            // Simulation update. Reclaim slots only after vanilla has removed the old
            // EnabledEffect buffer entry; otherwise a same-frame add can reuse a still-live
            // effectSlot and bind the new prefab to the old EnabledEffectData entry.
            m_Vfx.ReclaimReleasedSlots(anchorBuffer, dummyEffectBuffer);

            // Step 1: Expire old VFX effects (read valid index from anchor buffer, disable entry)
            m_Vfx.Expire(now, enabledData, vfxQueue, anchorBuffer, dummyEffectBuffer);

            // Step 2: Add new VFX requests (append to EnabledEffectData + anchor buffer)
            m_Vfx.AddPending(now, enabledData, vfxQueue, anchorBuffer, dummyEffectBuffer, m_Anchor.Entity,
                             ref m_NextEffectId, out int addedCount, out int lastEffectId, out int lastEffectSlot, out int lastEnabledIdx,
                             out Entity lastPrefab, out int lastVfxIndex);
            if (addedCount > 0)
                m_Diag.OnEffectAdded(currentFrame, lastEffectId, lastEffectSlot, lastEnabledIdx, lastPrefab, lastVfxIndex);

            if (m_Vfx.ActiveCount > 0)
                m_Diag.LogState("POST", enabledData, anchorBuffer, m_Vfx.ActiveCount, m_Vfx.PendingCount);
        }

        // Proven-visible impact effect. It shares the vanilla building-fire pool,
        // but the visibility trade-off is intentional: ExplosionTimedVFX renders
        // as a weak ~5-particle puff at the same scale/lifetime.
        private const string ExplosionVfxName = "FireBigVFX";

        // MuzzleFlash renders with a sparks effect instead of the building-fire pool so rapid
        // AA fire is not mistaken for the gun being on fire. Best-effort: if absent, the channel
        // falls back to the explosion prefab for MuzzleFlash.
        private const string MuzzleFlashVfxName = "SparksVFX";

        private void TryInitialize()
        {
            if (!m_EffectCache.IsInitialized) return;

            if (!m_EffectCache.TryGetEffect(ExplosionVfxName, out Entity vfxEntity) || vfxEntity == Entity.Null)
            {
                m_InitFailureFrames++;
                if (m_InitFailureFrames == MAX_INIT_FAILURE_FRAMES)
                {
                    Log.Error($"[VFX:7c] Initialization failed after {MAX_INIT_FAILURE_FRAMES} ready frames. Required effect '{ExplosionVfxName}' was not found");
                    Enabled = false;
                }
                return;
            }

            // CIVIC051: one-shot init operations on first ready frame, not hot path
#pragma warning disable CIVIC051
            if (!EntityManager.HasComponent<VFXData>(vfxEntity))
            {
                Log.Error($"[VFX] '{ExplosionVfxName}' has no VFXData component");
                Enabled = false;
                return;
            }

            var vfxData = EntityManager.GetComponentData<VFXData>(vfxEntity);
#pragma warning restore CIVIC051
            m_Vfx.SetPrefab(vfxEntity, vfxData.m_Index);

            // Bind the optional muzzle-flash effect (sparks). Non-fatal if missing — the channel
            // falls back to the explosion prefab for MuzzleFlash requests.
#pragma warning disable CIVIC051 // one-shot init: optional muzzle-flash effect bind on first ready frame
            if (m_EffectCache.TryGetEffect(MuzzleFlashVfxName, out Entity muzzleEntity)
                && muzzleEntity != Entity.Null
                && EntityManager.HasComponent<VFXData>(muzzleEntity))
            {
                var muzzleVfxData = EntityManager.GetComponentData<VFXData>(muzzleEntity);
                m_Vfx.SetMuzzleFlashPrefab(muzzleEntity, muzzleVfxData.m_Index);
                Log.Info($"[VFX:7c] MuzzleFlash effect bound: '{MuzzleFlashVfxName}' entity={muzzleEntity.Index}, vfxIndex={muzzleVfxData.m_Index}");
            }
            else
            {
                Log.Warn($"[VFX:7c] MuzzleFlash effect '{MuzzleFlashVfxName}' not found — AA fire falls back to '{ExplosionVfxName}'");
            }
#pragma warning restore CIVIC051

            m_InitFailureFrames = 0;

            m_Anchor.Create(EntityManager, ExplosionVfxChannel.MAX_CONCURRENT);

            m_Initialized = true;
            FlushPreInitSfxRequests();

            // CIVIC051: diagnostic post-init checks, gated by Log.IsDebugEnabled below
#pragma warning disable CIVIC051
            bool anchorHasBuffer = EntityManager.HasBuffer<EnabledEffect>(m_Anchor.Entity);
            bool anchorHasPrefabRef = EntityManager.HasComponent<PrefabRef>(m_Anchor.Entity);
            bool anchorHasPrefabTag = EntityManager.HasComponent<Prefab>(m_Anchor.Entity);
            bool dummyHasEffectBuf = EntityManager.HasBuffer<Game.Prefabs.Effect>(m_Anchor.DummyPrefab);
            int dummyEffectLen = dummyHasEffectBuf ? EntityManager.GetBuffer<Game.Prefabs.Effect>(m_Anchor.DummyPrefab).Length : -1;
            bool dummyHasPrefabTag = EntityManager.HasComponent<Prefab>(m_Anchor.DummyPrefab);
#pragma warning restore CIVIC051

            Log.Info($"[VFX:7c] Initialized: prefab='{ExplosionVfxName}' entity={vfxEntity.Index}, vfxIndex={vfxData.m_Index}, " +
                     $"maxCount={vfxData.m_MaxCount}, anchor={m_Anchor.Entity.Index}:{m_Anchor.Entity.Version}");
            Log.Debug($"[VFX:DIAG:INIT] Anchor: hasBuffer={anchorHasBuffer}, hasPrefabRef={anchorHasPrefabRef}, " +
                     $"hasPrefabTag={anchorHasPrefabTag}");
            Log.Debug($"[VFX:DIAG:INIT] DummyPrefab={m_Anchor.DummyPrefab.Index}:{m_Anchor.DummyPrefab.Version}: " +
                     $"hasEffectBuf={dummyHasEffectBuf}, effectBufLen={dummyEffectLen}, " +
                     $"hasPrefabTag={dummyHasPrefabTag}");
            Log.Debug($"[VFX:DIAG:INIT] ExplosionPrefab={vfxEntity.Index}: " +
                     $"anchor PrefabRef points to dummy={m_Anchor.DummyPrefab.Index} (NOT explosion prefab)");
        }

        /// <summary>
        /// Reset state after load. Runtime entities don't survive deserialization.
        /// EnabledEffectData is cleared by EffectControlSystem.PostDeserialize,
        /// VFXSystem is reset by VFXSystem.PreDeserialize.
        /// </summary>
        private void ResetForLoadBoundary(string logMessage)
        {
            ResetTransientState(logMessage, invalidateEffectCache: true, clearPendingRequests: true);
        }

        /// <summary>
        /// Mid-session anchor loss should rebuild only our runtime anchor. Do not
        /// invalidate EffectCacheSystem here: prefab cache entries survive ordinary
        /// runtime entity deletion, and wiping them creates avoidable SFX/VFX drops
        /// while the cache repopulates asynchronously.
        ///
        /// Triggered on every save cycle: VanillaVfxSerializationBoundarySystem
        /// tags the anchor Deleted, CleanUpSystem destroys it, next OnUpdateImpl
        /// sees the missing anchor and lands here.
        ///
        /// m_NextEffectId is NOT reset here. Effect ids identify entries in
        /// EnabledEffectData; reusing id=0 after a save collides with any still-
        /// referenced entries from the previous anchor era. The id sequence is
        /// bounded by int range — reset only on full load via ResetTransientState
        /// (see m_NextEffectId = 0 there).
        ///
        /// Recovery never writes new/expire VFX entries through a Deleted-but-live
        /// anchor. Any still-live entities are marked Deleted and forgotten; vanilla
        /// CleanUpSystem / CompleteEnabledSystem own the actual destruction path.
        /// </summary>
        private void RecoverMissingAnchor()
        {
            m_Vfx.ResetForAnchorRecreate();
            m_Diag.Reset();
            // NOTE: m_NextEffectId intentionally preserved across rebuild.
            m_InitFailureFrames = 0;
            m_Initialized = false;

            m_Anchor.MarkDeletedAndForget(EntityManager);

            Log.Warn("[VFX:7c] Runtime anchor missing — rebuild on next TryInitialize");
        }

        private void ResetTransientState(string logMessage, bool invalidateEffectCache, bool clearPendingRequests)
        {
            if (invalidateEffectCache)
                m_EffectCache.InvalidateCache();

            m_Vfx.Reset();
            if (clearPendingRequests)
            {
                m_Sfx.Reset();
                m_PendingSfxBeforeInit.Clear();
            }
            m_Diag.Reset();
            m_NextEffectId = 0;
            m_InitFailureFrames = 0;

            // S22-H2 FIX: Force TryInitialize to re-run — stale prefab/vfxIndex
            // from previous session will silently fail to render VFX after load.
            m_Initialized = false;

            // Clean up stale entities if they survived load
            m_Anchor.Destroy(EntityManager);

            Log.Info(logMessage);
        }

        protected override void OnDestroy()
        {
            m_Vfx.Reset();
            m_Sfx.Reset();
            m_Diag.Reset();
            m_PendingSfxBeforeInit.Clear();
            m_InitFailureFrames = 0;
            m_Initialized = false;

            m_Anchor.Destroy(EntityManager);

            if (m_OwnerAttachQueue.IsCreated)
                m_OwnerAttachQueue.Dispose();

            Log.Info("Destroyed");
            base.OnDestroy();
        }

        private void FlushPreInitSfxRequests()
        {
            if (m_PendingSfxBeforeInit.Count == 0)
                return;

            for (int i = 0; i < m_PendingSfxBeforeInit.Count; i++)
            {
                var request = m_PendingSfxBeforeInit[i];
                if (m_Sfx.PendingCount >= OneShotSfxChannel.MAX_PENDING_REQUESTS)
                {
                    Log.Warn($"[SFX] DROPPED pre-init flush request (pending full: {m_Sfx.PendingCount}/{OneShotSfxChannel.MAX_PENDING_REQUESTS})");
                    break;
                }

                if (m_EffectCache.TryGetSfx(request.Name, out Entity prefab))
                    m_Sfx.Enqueue(prefab, request.Position);
                else if (Log.IsDebugEnabled)
                    Log.Debug($"[SFX] Audio effect not found or invalid after init: {request.Name}");
            }

            m_PendingSfxBeforeInit.Clear();
        }
    }
}
