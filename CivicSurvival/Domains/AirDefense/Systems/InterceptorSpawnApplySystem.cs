using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Constants;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Consumer half of the interceptor-missile spawn split (mirror of <c>ThreatSpawnApplySystem</c>).
    /// Runs in Modification4: reads <see cref="InterceptorSpawnIntent"/> elements recorded by
    /// <c>InterceptorSpawnSystem</c> in GameSimulation and performs the actual
    /// <c>CreateEntity(interceptor render archetype)</c> + component writes from THIS phase — where
    /// vanilla's render batch pipeline (RequiredBatchesSystem ModificationEnd, PreCullingSystem,
    /// BatchManagerSystem) expects the new Moving chunk.
    ///
    /// <para><b>Render-completion gate (CIVIC508).</b> <c>CreateEntity</c> migrates into the interceptor
    /// archetype. InterceptorMovementSystem now writes Transform/Moving/TransformFrame from a Burst
    /// <c>InterceptorRenderWriteJob</c> whose handle is kept out of <c>Dependency</c> — that worker may be
    /// reading the interceptor chunks while this CreateEntity migrates them. So this system drains the
    /// published render handle via <c>IRenderWriteBarrier.Consume</c> before the CreateEntity loop (same
    /// contract as the threat spawn consumer). The interceptor archetype still carries no
    /// <c>ThreatPosition</c>/<c>Shahed</c>/<c>Ballistic</c>, so it never shares a chunk with
    /// <c>DroneRenderWriteJob</c> — the producer it must wait on is the INTERCEPTOR render job, not the
    /// drone one (the masks are disjoint by archetype).</para>
    ///
    /// Pause-safe: the producer lives in GameSimulation (no tick in pause), so no intent is recorded
    /// in pause; this consumer just sees an empty buffer and does nothing.
    /// </summary>
    [ActIndependent]
    public partial class InterceptorSpawnApplySystem : CivicSystemBase, ICivicSingletonOwner<InterceptorSpawnIntentHost>
    {
        private static readonly LogContext Log = new("InterceptorSpawnApplySystem");

        private EntityQuery m_IntentQuery;
        private ModificationBarrier4 m_ModificationBarrier = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;
        private EntityArchetype m_InterceptorArchetype;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<InterceptorSpawnIntent>());
            m_ModificationBarrier = World.GetOrCreateSystemManaged<ModificationBarrier4>();

            // BRG Moving archetype — vanilla MovingObjectPrefab pattern, minus threat-specific tags.
#pragma warning disable CIVIC487 // Interceptor archetype persists across save/load; InterceptorLoadPurgeSystem despawns restored missiles before first PreCulling on load (transient visual).
            m_InterceptorArchetype = EntityManager.CreateArchetype(
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
                ComponentType.ReadWrite<Interceptor>(),
                ComponentType.ReadWrite<InterceptorTag>()
            );
#pragma warning restore CIVIC487

            RequireForUpdate(m_IntentQuery);
            InterceptorSpawnIntentHost.EnsureExists(EntityManager);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // CIVIC403: resolve infrastructure services in OnStartRunning (??=), not OnCreate.
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            InterceptorSpawnIntentHost.EnsureExists(EntityManager);
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            InterceptorSpawnIntentHost.EnsureExists(entityManager);
        }

        protected override void OnUpdateImpl()
        {
            if (!m_IntentQuery.TryGetSingletonBuffer<InterceptorSpawnIntent>(out var intentBuffer, isReadOnly: true)
                || intentBuffer.Length == 0)
                return;

            int count = intentBuffer.Length;
            var intents = new NativeArray<InterceptorSpawnIntent>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
                intents[i] = intentBuffer[i];

            // CIVIC508: drain the in-flight interceptor render job before CreateEntity migrates into the
            // interceptor archetype under the worker. Self-prunes completed handles (no-op when idle);
            // same gate as the threat spawn consumer.
            m_RenderWriteBarrier.Consume(GetType(), RenderWriteComponentMask.InterceptorRender);

            EntityCommandBuffer ecb = m_ModificationBarrier.CreateCommandBuffer();

            // No WaveBatchKey idempotency guard (unlike ThreatSpawnApplySystem): each intent is fully
            // self-contained (one missile, no per-group event), the buffer is Clear()ed in the same pass
            // right below, and the producer re-appends afresh each frame — so a buffer surviving an extra
            // sim-tick at 2x-8x before the clear flushes cannot re-apply a stale group here. A duplicated
            // missile would be a harmless cosmetic double, not gameplay state (PvP-safe either way).
            try
            {
                for (int i = 0; i < count; i++)
                    ApplyInterceptor(ecb, intents[i]);
            }
            finally
            {
                intents.Dispose();
            }

            if (m_IntentQuery.TryGetSingletonBuffer<InterceptorSpawnIntent>(out var rwBuffer, isReadOnly: false))
                rwBuffer.Clear();

            if (count > 0 && Log.IsDebugEnabled)
                Log.Debug($"[Interceptor] spawned {count} missile(s)");
        }

        private void ApplyInterceptor(EntityCommandBuffer ecb, in InterceptorSpawnIntent intent)
        {
            // RENDER-SAFE-SPAWN: render-archetype CreateEntity is in-phase here (Modification4,
            // before the vanilla render batch pass). Never move to GameSimulation (out-of-phase
            // migration into the live render pipeline crashes — same class as the threat spawn).
            Entity entity = ecb.CreateEntity(m_InterceptorArchetype);
            ecb.SetSharedComponent(entity, new UpdateFrame(ThreatConstants.TMS_SUB_FRAME));
            ecb.SetName(entity, "Interceptor");

            ecb.SetComponent(entity, new PrefabRef { m_Prefab = new Entity { Index = intent.PrefabIndex, Version = intent.PrefabVersion } });
            ecb.SetComponent(entity, new Game.Objects.Transform { m_Position = intent.SpawnPos, m_Rotation = intent.Rotation });
            ecb.SetComponent(entity, new Moving { m_Velocity = float3.zero }); // static in Phase 1; movement system drives it in Phase 2
            ecb.SetComponent(entity, new PseudoRandomSeed(intent.PseudoSeed));

            int subMeshCount = math.max(1, intent.SubMeshCount);
            for (int i = 0; i < subMeshCount; i++)
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

            // Pre-populate TransformFrame (4 OIS slots). Static missile → all slots at spawn pos so
            // Bezier interpolation sees a consistent zero-velocity history from frame 0.
            for (int i = 0; i < 4; i++)
            {
                ecb.AppendToBuffer(entity, new TransformFrame
                {
                    m_Position = intent.SpawnPos,
                    m_Velocity = float3.zero,
                    m_Rotation = intent.Rotation
                });
            }

            ecb.SetComponent(entity, new Interceptor
            {
                SpawnPos = intent.SpawnPos,
                CurrentPosition = intent.SpawnPos, // seed before first movement tick (exhaust reads this, not Transform)
                TargetPos = intent.TargetPos,
                ThreatIndex = intent.ThreatIndex,
                ThreatVersion = intent.ThreatVersion,
                Speed = intent.Speed,
                Source = intent.Source,
                LaunchFrame = intent.LaunchFrame,
                ThreatGeneration = intent.ThreatGeneration,
                IsBallistic = intent.IsBallistic
            });
            // InterceptorTag / Object / CullingInfo / MeshBatch / ObjectGeometry /
            // InterpolatedTransform / Created / Updated come from the archetype default.
        }
    }
}
