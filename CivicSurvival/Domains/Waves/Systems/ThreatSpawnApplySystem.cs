using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Constants;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// Consumer half of the off-barrier spawn split (mirror of <c>ThreatDeletionApplySystem</c>
    /// and <c>ModFireApplySystem</c>). Runs in Modification4: reads <see cref="ThreatSpawnIntent"/>
    /// elements recorded by <c>ThreatSpawnSystem</c> in GameSimulation and performs the actual
    /// <c>CreateEntity(drone/ballistic render archetype)</c> + component writes from THIS phase —
    /// where vanilla's render batch pipeline (<c>RequiredBatchesSystem</c> ModificationEnd,
    /// <c>PreCullingSystem</c>, <c>BatchManagerSystem</c>, all later in the same MainLoop) expects
    /// the new chunk.
    ///
    /// Why the producer cannot create the drone itself: creating a drone/ballistic render entity
    /// migrates a render chunk that <c>DroneRenderWriteJob</c> iterates. Doing that from
    /// GameSimulation (where the render job is scheduled and still in flight) lands the migration
    /// into a live render chunk → native AV (crash <c>9db2bedf</c>). This consumer first drains
    /// the render writer through <see cref="IRenderWriteBarrier.Consume"/> (the render-completion
    /// gate), then creates the entities — so the render job is never in flight during the
    /// structural change. By Modification4 of frame N+1 the render job scheduled in
    /// GameSimulation of frame N has had a whole frame of worker time, so the gate is near-free
    /// (it does not re-introduce the same-frame main-thread block the old barrier Complete cost).
    ///
    /// Pause-safe: the producer lives in GameSimulation, which does not tick while paused, so no
    /// intent is ever recorded in pause; this consumer (Modification4, which DOES tick in pause)
    /// just sees an empty buffer and does nothing.
    /// </summary>
    [ActIndependent]
    public partial class ThreatSpawnApplySystem : CivicSystemBase, ICivicSingletonOwner<ThreatSpawnIntentHost>
    {
        private static readonly LogContext Log = new("ThreatSpawnApplySystem");

        // Same render-write scope the producer (DroneRenderWriteJob) publishes under. The full
        // mask (all 3 components) is consumed so the single published render handle is drained
        // regardless of which sub-mask a future split might use.
        private const RenderWriteComponentMask RenderWriteMask =
            RenderWriteComponentMask.ThreatTransform |
            RenderWriteComponentMask.ThreatMoving |
            RenderWriteComponentMask.ThreatTransformFrame;

        private const float FIXED_TIME_STEP = 4f / 15f;

        private EntityQuery m_IntentQuery;
        private ModificationBarrier4 m_ModificationBarrier = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;

        // BRG rendering archetypes — moved here from ThreatSpawnSystem (the consumer owns the
        // CreateEntity now). Built once in OnCreate (Axiom 7). Threat archetypes persist across
        // save/load (C1); ThreatLoadRenderReinitSystem reinitializes render-state + lifecycle
        // tags on load.
        private EntityArchetype m_DroneArchetype;
        private EntityArchetype m_BallisticArchetype;

        // Idempotency safety: every WaveBatchKey whose group has been applied this session.
        // CS2 barriers play back after all sim ticks, so at 2x-8x the intent buffer can still be
        // alive on a later tick of the same frame before the clear flushes — this stops a group
        // re-apply. A single multi-group buffer (e.g. a wave + an outbound strike landing in the
        // same frame, each with its own key) must protect ALL its groups, not just the last one
        // applied — so the applied keys are tracked in a set, not a single field. Persistent so it
        // survives between ticks of the same frame; cleared on load (the buffer is stripped there).
        private NativeHashSet<uint> m_AppliedBatchKeys;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<ThreatSpawnIntent>());
            m_ModificationBarrier = World.GetOrCreateSystemManaged<ModificationBarrier4>();
            m_AppliedBatchKeys = new NativeHashSet<uint>(8, Allocator.Persistent);

            // BRG drone archetype — vanilla MovingObjectPrefab pattern (motion vectors via BRG).
#pragma warning disable CIVIC487 // Threat archetypes persist across save/load (C1); ThreatLoadRenderReinitSystem reinitializes render-state + lifecycle tags on load.
            m_DroneArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Objects.Object>(),
                ComponentType.ReadWrite<Game.Objects.Transform>(),
                ComponentType.ReadWrite<Game.Objects.Moving>(),
                ComponentType.ReadWrite<Game.Rendering.CullingInfo>(),
                ComponentType.ReadWrite<Game.Rendering.MeshBatch>(),
                ComponentType.ReadWrite<Game.Simulation.UpdateFrame>(),
                ComponentType.ReadWrite<Game.Objects.TransformFrame>(),
                ComponentType.ReadWrite<Game.Rendering.InterpolatedTransform>(),
                ComponentType.ReadWrite<PrefabRef>(),
                ComponentType.ReadWrite<Created>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<ObjectGeometry>(),
                ComponentType.ReadWrite<PseudoRandomSeed>(),
                ComponentType.ReadWrite<MeshColor>(),
                ComponentType.ReadWrite<Shahed>(),
                ComponentType.ReadWrite<ShahedCombatState>(),
                ComponentType.ReadWrite<ThreatPosition>(),
                ComponentType.ReadWrite<ThreatFlightProgress>(),
                ComponentType.ReadWrite<ActiveThreat>(),
                ComponentType.ReadWrite<PendingDestruction>(),
                ComponentType.ReadWrite<PendingThreatDeletion>(),
                ComponentType.ReadWrite<PlayerOutboundThreat>(),
                ComponentType.ReadWrite<OutboundStrikePayload>(),
                ComponentType.ReadWrite<IdentifiedTarget>()
            );

            m_BallisticArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Objects.Object>(),
                ComponentType.ReadWrite<Game.Objects.Transform>(),
                ComponentType.ReadWrite<Game.Objects.Moving>(),
                ComponentType.ReadWrite<Game.Rendering.CullingInfo>(),
                ComponentType.ReadWrite<Game.Rendering.MeshBatch>(),
                ComponentType.ReadWrite<Game.Simulation.UpdateFrame>(),
                ComponentType.ReadWrite<Game.Objects.TransformFrame>(),
                ComponentType.ReadWrite<Game.Rendering.InterpolatedTransform>(),
                ComponentType.ReadWrite<PrefabRef>(),
                ComponentType.ReadWrite<Created>(),
                ComponentType.ReadWrite<Updated>(),
                ComponentType.ReadWrite<ObjectGeometry>(),
                ComponentType.ReadWrite<PseudoRandomSeed>(),
                ComponentType.ReadWrite<MeshColor>(),
                ComponentType.ReadWrite<Game.Effects.EnabledEffect>(),
                ComponentType.ReadWrite<Ballistic>(),
                ComponentType.ReadWrite<BallisticInterceptState>(),
                ComponentType.ReadWrite<ThreatPosition>(),
                ComponentType.ReadWrite<ThreatFlightProgress>(),
                ComponentType.ReadWrite<ActiveThreat>(),
                ComponentType.ReadWrite<PendingDestruction>(),
                ComponentType.ReadWrite<PendingThreatDeletion>(),
                ComponentType.ReadWrite<PlayerOutboundThreat>(),
                ComponentType.ReadWrite<OutboundStrikePayload>()
            );
#pragma warning restore CIVIC487

            RequireForUpdate(m_IntentQuery);
            // Host must exist before the producer (GameSimulation) first appends — create it here
            // and re-create it on every start/load (OnCreate doesn't re-run on a fresh-world load,
            // and the non-serialized host is stripped on load). The triple OnCreate +
            // OnStartRunning + OnLoadRestore avoids CIVIC414 (only-OnCreate) and CIVIC423
            // (only-OnStartRunning).
            ThreatSpawnIntentHost.EnsureExists(EntityManager);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            ThreatSpawnIntentHost.EnsureExists(EntityManager);
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            // The intent host is not serialized — recreate the empty host so the producer can
            // append after load. The buffer is empty by design (a save in the producer→consumer
            // gap re-issues the wave through the WaveExecutor resume path, not a stale intent).
            ThreatSpawnIntentHost.EnsureExists(entityManager);
            m_AppliedBatchKeys.Clear();
        }

        protected override void OnDestroy()
        {
            if (m_AppliedBatchKeys.IsCreated)
                m_AppliedBatchKeys.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            // Idle early-out BEFORE the gate: the host buffer is present every tick (so the query is
            // never empty), but it is length 0 except on a spawn frame. Skip the render-completion
            // drain when there is nothing to spawn — no structural change ⇒ no race to guard.
            if (!m_IntentQuery.TryGetSingletonBuffer<ThreatSpawnIntent>(out var intentBuffer, isReadOnly: true)
                || intentBuffer.Length == 0)
                return;

            // RENDER-COMPLETION GATE (CRITICAL): drain the in-flight DroneRenderWriteJob BEFORE
            // any structural change on the drone/ballistic render archetype. The render handle is
            // kept out of ThreatMovementSystem.Dependency, so without this the CreateEntity below
            // could migrate a chunk the render job is still iterating → native AV (9db2bedf).
            // Full mask (all 3 components) — one published handle, drained regardless of split.
            m_RenderWriteBarrier.Consume(GetType(), RenderWriteMask);

            // Per-drain reset (CIVIC187): batch-key idempotency only needs to dedup groups WITHIN
            // this drain. The buffer is cleared immediately after, so a later sim tick sees an empty
            // buffer and early-returns; clearing here bounds the set to one drain's keys.
            m_AppliedBatchKeys.Clear();

            // Snapshot the intents into Temp so the buffer can be cleared (structural-free) after
            // the deferred CreateEntity commands are queued. The buffer lives ≤1 frame.
            int count = intentBuffer.Length;
            var intents = new NativeArray<ThreatSpawnIntent>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
                intents[i] = intentBuffer[i];

            EntityCommandBuffer ecb = m_ModificationBarrier.CreateCommandBuffer();

            // Per-WaveBatchKey accumulation for ThreatsSpawnedEvent. A single SpawnWave call
            // shares one key; a second SpawnWave that landed in the same buffer before this
            // consumer ran has its own key and gets its own event.
            uint currentKey = 0;
            int currentWave = 0;
            int currentShaheds = 0;
            int currentBallistics = 0;
            bool haveGroup = false;
            int appliedShaheds = 0;
            int appliedBallistics = 0;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var intent = intents[i];

                    if (haveGroup && intent.WaveBatchKey != currentKey)
                    {
                        PublishSpawned(currentShaheds, currentBallistics, currentWave);
                        // Record the just-finished group's key AFTER all its intents were applied.
                        // All intents of one wave share a single WaveBatchKey, so recording it
                        // per-intent (before the rest of the group) would skip every intent after
                        // the first — the "1 threat spawned" regression. Recorded here, a duplicate
                        // group with the same key later in this buffer is still skipped wholesale.
                        if (currentKey != 0)
                            m_AppliedBatchKeys.Add(currentKey);
                        currentShaheds = 0;
                        currentBallistics = 0;
                    }

                    currentKey = intent.WaveBatchKey;
                    currentWave = intent.WaveNumber;
                    haveGroup = true;

                    // Idempotency safety: skip a group whose key was already recorded (a duplicate
                    // group earlier in this same buffer). The key is recorded only once the whole
                    // group finishes (above / after the loop), so intents WITHIN a group are never
                    // skipped.
                    if (intent.WaveBatchKey != 0 && m_AppliedBatchKeys.Contains(intent.WaveBatchKey))
                        continue;

                    if (intent.Kind == 0)
                    {
                        ApplyShahed(ecb, intent);
                        currentShaheds++;
                        appliedShaheds++;
                    }
                    else
                    {
                        ApplyBallistic(ecb, intent);
                        currentBallistics++;
                        appliedBallistics++;
                    }
                }

                if (haveGroup)
                {
                    PublishSpawned(currentShaheds, currentBallistics, currentWave);
                    // Record the final group's key (same rationale as the group-boundary case).
                    if (currentKey != 0)
                        m_AppliedBatchKeys.Add(currentKey);
                }
            }
            finally
            {
                intents.Dispose();
            }

            // Clear the consumed intents (DynamicBuffer.Clear is not a structural change). Fetch a
            // fresh RW handle — the gate above and the CreateEntity queue do not invalidate it.
            if (m_IntentQuery.TryGetSingletonBuffer<ThreatSpawnIntent>(out var rwBuffer, isReadOnly: false))
                rwBuffer.Clear();

            if ((appliedShaheds > 0 || appliedBallistics > 0) && Log.IsDebugEnabled)
                Log.Debug($"applied spawn intents: {appliedShaheds} Shahed(s), {appliedBallistics} Ballistic(s)");
        }

        private void PublishSpawned(int shaheds, int ballistics, int waveNumber)
        {
            if (shaheds == 0 && ballistics == 0)
                return;
            // Published AFTER the real CreateEntity is queued (not in the producer before the
            // entity exists) — a save in the producer→consumer gap never records a wave as
            // spawned while its entities do not exist.
            EventBus?.SafePublish(new ThreatsSpawnedEvent(shaheds, ballistics, waveNumber), "ThreatSpawnApplySystem");
        }

        private void ApplyShahed(EntityCommandBuffer ecb, in ThreatSpawnIntent intent)
        {
            // RENDER-SAFE-SPAWN: drone render-archetype CreateEntity is in-phase here
            // (Modification4, after the render-completion gate, before the render batch pass).
            // CIVIC499 allow-marker — never move this to GameSimulation (out-of-phase migration
            // into a live render chunk crashes DroneRenderWriteJob with a native AV).
            Entity entity = ecb.CreateEntity(m_DroneArchetype);
            ecb.SetComponentEnabled<PendingDestruction>(entity, false);
            ecb.SetComponentEnabled<PendingThreatDeletion>(entity, false);
            // Faction: enabled only for the player's outbound counter-strike. Wave threats spawn
            // with intent.Faction == FactionEnemyInbound (0) → bit disabled → pipeline behavior
            // unchanged. Enableable bit, not a structural change.
            bool isOutbound = intent.Faction == ThreatSpawnIntent.FactionPlayerOutbound;
            ecb.SetComponentEnabled<PlayerOutboundThreat>(entity, isOutbound);
            // Outbound axis payload: carries the enemy-axis hit to arrival. Default (0 damage,
            // IsOutbound=false) for inbound waves — they never read it (the faction gate routes
            // inbound to city terminalization, not the axis channel). IsOutbound is serialized
            // (the faction tag is not) so a mid-flight save/load can re-assert the stripped tag.
            ecb.SetComponent(entity, new OutboundStrikePayload { Axis = intent.OutboundAxis, Damage = intent.OutboundDamage, IsOutbound = isOutbound, Seed = intent.OutboundSeed });
            ecb.SetSharedComponent(entity, new UpdateFrame(ThreatConstants.TMS_SUB_FRAME));

            ecb.SetComponent(entity, new PrefabRef { m_Prefab = ResolvePrefab(intent) });
            ecb.SetComponent(entity, new Game.Objects.Transform { m_Position = intent.SpawnPos, m_Rotation = intent.Rotation });
            ecb.SetComponent(entity, new Moving { m_Velocity = intent.Velocity });
            ecb.SetComponent(entity, new PseudoRandomSeed(intent.PseudoSeed));

            AppendMeshColors(ecb, entity, intent.SubMeshCount);

            // TransformFrame buffer preseed (4 slots). NOT load-bearing for OOB-safety and the
            // velocity-offset history does NOT survive to the renderer: UpdateGroupSystem
            // (Modification5) overwrites all 4 slots with zero-velocity copies of the spawn
            // transform on the Created frame, before ObjectInterpolateSystem (Rendering) reads
            // frames[idx&3] (decompile: SystemOrder.cs:214/658, UpdateGroupSystem.cs:471-483).
            // An empty buffer is equally safe — see the ballistic path, which skips this preseed.
            // Kept here only as explicit init.
            for (int i = 0; i < 4; i++)
            {
                float timeBack = (3 - i) * FIXED_TIME_STEP;
                ecb.AppendToBuffer(entity, new TransformFrame
                {
                    m_Position = intent.SpawnPos - intent.Velocity * timeBack,
                    m_Velocity = intent.Velocity,
                    m_Rotation = intent.Rotation
                });
            }

            ecb.SetName(entity, "Shahed");

            ecb.SetComponent(entity, new Shahed
            {
                SpawnPosition = intent.SpawnPos,
                TargetPosition = intent.TargetPos,
                TargetBuilding = new BuildingRef(intent.TargetBuildingIndex, intent.TargetBuildingVersion),
                Speed = intent.Speed,
                CurrentDistance = 0f,
                TotalDistance = intent.TotalDistance,
                InterceptPosition = float3.zero,
                TargetCategory = intent.Category,
                LastCheckpointPos = intent.SpawnPos,
                TimeSinceCheckpoint = 0f,
                ThreatGeneration = intent.ThreatGeneration
            });

            ecb.SetComponent(entity, new ShahedCombatState { IsFocusStrike = intent.IsFocusStrike });
            ecb.SetComponent(entity, new ThreatPosition(intent.SpawnPos, intent.Rotation));
            ecb.SetComponent(entity, new ThreatFlightProgress
            {
                MinDistanceToTarget = float.MaxValue,
                MinDistanceTime = intent.SpawnElapsedTime
            });
            // ActiveThreat / IdentifiedTarget / Created / Updated / Object / CullingInfo /
            // MeshBatch / ObjectGeometry / InterpolatedTransform come from the archetype default.
            // Created/Updated are load-bearing — vanilla PreCulling rebuilds MeshBatch off them.
        }

        private void ApplyBallistic(EntityCommandBuffer ecb, in ThreatSpawnIntent intent)
        {
            // RENDER-SAFE-SPAWN: ballistic render-archetype CreateEntity is in-phase here
            // (Modification4, after the render-completion gate, before the render batch pass).
            // CIVIC499 allow-marker — never move this to GameSimulation.
            Entity entity = ecb.CreateEntity(m_BallisticArchetype);
            ecb.SetComponentEnabled<PendingDestruction>(entity, false);
            ecb.SetComponentEnabled<PendingThreatDeletion>(entity, false);
            // Faction: enabled only for the player's outbound counter-strike. Wave threats spawn
            // with intent.Faction == FactionEnemyInbound (0) → bit disabled → pipeline behavior
            // unchanged. Enableable bit, not a structural change.
            bool isOutbound = intent.Faction == ThreatSpawnIntent.FactionPlayerOutbound;
            ecb.SetComponentEnabled<PlayerOutboundThreat>(entity, isOutbound);
            // Outbound axis payload (see ApplyShahed) — default/0 for inbound waves; IsOutbound
            // is serialized so a mid-flight save/load can re-assert the stripped faction tag.
            ecb.SetComponent(entity, new OutboundStrikePayload { Axis = intent.OutboundAxis, Damage = intent.OutboundDamage, IsOutbound = isOutbound, Seed = intent.OutboundSeed });
            ecb.SetSharedComponent(entity, new UpdateFrame(ThreatConstants.TMS_SUB_FRAME));
            ecb.SetName(entity, "Ballistic");

            ecb.SetComponent(entity, new PrefabRef { m_Prefab = ResolvePrefab(intent) });
            ecb.SetComponent(entity, new Game.Objects.Transform { m_Position = intent.SpawnPos, m_Rotation = intent.Rotation });
            ecb.SetComponent(entity, new Moving { m_Velocity = intent.Velocity });
            ecb.SetComponent(entity, new PseudoRandomSeed(intent.PseudoSeed));

            AppendMeshColors(ecb, entity, intent.SubMeshCount);

            // No TransformFrame preseed. An empty (length-0) buffer is OOB-safe: UpdateGroupSystem
            // (Modification5) calls ResizeUninitialized(4) and fills every Created+InterpolatedTransform
            // entity's buffer before ObjectInterpolateSystem (Rendering) reads frames[idx&3]. The spawn
            // happens at ModificationBarrier4 (Modification4), one phase before that resize, so no reader
            // ever sees the length-0 buffer (decompile: SystemOrder.cs:214/658, UpdateGroupSystem.cs:471-483;
            // ref Docs/Reference/Rendering/VANILLA_RENDER_PIPELINE.md §3.9). Mirroring the drone preseed
            // here would be dead code — UpdateGroupSystem overwrites it the same frame.

            ecb.SetComponent(entity, new Ballistic
            {
                SpawnPosition = intent.SpawnPos,
                TargetPosition = intent.TargetPos,
                TargetBuilding = new BuildingRef(intent.TargetBuildingIndex, intent.TargetBuildingVersion),
                Speed = intent.Speed,
                ImpactRadius = intent.ImpactRadius,
                DamageSeverity = intent.DamageSeverity,
                ThreatGeneration = intent.ThreatGeneration
            });

            ecb.SetComponent(entity, new ThreatPosition(intent.SpawnPos, intent.Rotation));
            ecb.SetComponent(entity, new ThreatFlightProgress
            {
                MinDistanceToTarget = float.MaxValue,
                MinDistanceTime = intent.SpawnElapsedTime
            });
            // BallisticInterceptState (default) + empty EnabledEffect buffer come from the
            // archetype; the exhaust controller in ThreatSpawnSystem attaches the VFX on its next
            // pass. ActiveThreat / Created / Updated / etc. come from the archetype default.
        }

        private static Entity ResolvePrefab(in ThreatSpawnIntent intent)
            => new Entity { Index = intent.PrefabIndex, Version = intent.PrefabVersion };

        private static void AppendMeshColors(EntityCommandBuffer ecb, Entity entity, byte subMeshCount)
        {
            int n = math.max(1, subMeshCount);
            for (int i = 0; i < n; i++)
            {
                ecb.AppendToBuffer(entity, new MeshColor
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
    }
}
