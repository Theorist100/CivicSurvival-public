using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Bootstrap;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Producer half of the interceptor-missile spawn split. Subscribes to <c>AAFireEvent</c> and,
    /// for Patriot shots only, records an <see cref="InterceptorSpawnIntent"/> on the singleton host
    /// buffer. The consumer (<c>InterceptorSpawnApplySystem</c>, Modification4) does the actual
    /// render-archetype <c>CreateEntity</c> from the render-safe phase.
    ///
    /// Visual-only: the missile mirrors the shot the fire-control formula already resolved; it never
    /// touches gameplay state. Pause-safe by construction — AA does not fire in pause and this
    /// producer lives in GameSimulation, which does not tick in pause, so no intent is recorded then.
    ///
    /// Graceful fallback: if the AIM120 prefab is absent (the .cok was not imported into the Asset
    /// Editor), <c>InterceptorEntity</c> is Null and the queued shots are simply dropped — tracers
    /// and the intercept formula keep working, only the missile model is missing.
    /// </summary>
    [ActIndependent]
    public partial class InterceptorSpawnSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("InterceptorSpawnSystem");

        /// <summary>Missile cruise speed (m/s). Faster than threats so it runs them down, but slow
        /// enough to read the flight + the smooth turn arc.</summary>
        private const float INTERCEPTOR_SPEED = 450f;

        /// <summary>Vertical launch offset (m) above the AA's base position, so the missile clears the
        /// Patriot's body/launcher and appears to leave the cell instead of spawning inside the chassis.</summary>
        private const float LAUNCH_HEIGHT_OFFSET = 12f;

        private const int MAX_QUEUED_SPAWN_EVENTS = 32;

        /// <summary>Blittable copy of AAFireEvent (a record = reference type) for the NativeQueue.</summary>
        private struct InterceptorSpawnData
        {
            public float3 AAPosition;
            public float3 ThreatPosition;
            public int ThreatIndex;
            public int ThreatVersion;
            public bool IsBallistic;
        }

        private NativeQueue<InterceptorSpawnData> m_SpawnQueue;
        private EntityQuery m_IntentQuery;
        private CivicPrefabInitSystem m_PrefabInit = null!;
        private SimulationSystem m_SimulationSystem = null!;
        private Unity.Mathematics.Random m_Random;
        [System.NonSerialized] private ThreatGenerationClock m_GenerationClock = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SpawnQueue = new NativeQueue<InterceptorSpawnData>(Allocator.Persistent);
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<InterceptorSpawnIntent>());
            m_PrefabInit = World.GetExistingSystemManaged<CivicPrefabInitSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            // Visual-only seed (PseudoRandomSeed for BRG colour variation); not serialized.
            m_Random = new Unity.Mathematics.Random((uint)(System.Environment.TickCount ^ 0xA1A0));

            SubscribeRequired<AAFireEvent>(OnAAFire);
            Log.Info("Created");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<AAFireEvent>(OnAAFire);
            if (m_SpawnQueue.IsCreated)
                m_SpawnQueue.Dispose();
            base.OnDestroy();
        }

        private void OnAAFire(AAFireEvent evt)
        {
            // Only missile launchers spawn an interceptor; guns (Bofors/Gepard/Heritage) stay on
            // tracers. Weapon class is owned by AATypeWeapon — the inverse gate is in TracerSpawnSystem.
            if (!evt.AAType.FiresInterceptorMissile())
                return;

            while (m_SpawnQueue.Count >= MAX_QUEUED_SPAWN_EVENTS && m_SpawnQueue.TryDequeue(out _))
            {
                // Drop oldest unresolved visual events when the bounded queue is full.
            }

            m_SpawnQueue.Enqueue(new InterceptorSpawnData
            {
                AAPosition = evt.AAPosition,
                ThreatPosition = evt.ThreatPosition,
                ThreatIndex = evt.ThreatIndex,
                ThreatVersion = evt.ThreatVersion,
                IsBallistic = evt.IsBallistic
            });
        }

        protected override void OnUpdateImpl()
        {
            if (m_SpawnQueue.Count == 0)
                return;

            Entity interceptorPrefab = m_PrefabInit.InterceptorEntity;
            if (interceptorPrefab == Entity.Null)
            {
                // Fallback: AIM120.cok not imported — drop the queue, keep tracers/formula working.
                m_SpawnQueue.Clear();
                return;
            }

            if (!m_IntentQuery.TryGetSingletonBuffer<InterceptorSpawnIntent>(out var buffer, isReadOnly: false))
                return; // host not created yet — retry next frame

            byte subMeshCount = (byte)math.min(byte.MaxValue, GetPrefabSubMeshCount(interceptorPrefab));
            uint launchFrame = m_SimulationSystem.frameIndex;
            // Stamp the threat's generation onto the missile at spawn (managed service read — no ECS
            // sync) so the resolution triggers never RO-read Shahed/Ballistic (perf H1). For a live
            // intercept clock.Current == the threat's own generation.
            m_GenerationClock ??= ServiceRegistry.Instance.Require<ThreatGenerationClock>();
            int generation = m_GenerationClock.Current;

            int spawned = 0;
            while (m_SpawnQueue.TryDequeue(out var data))
            {
                float3 delta = data.ThreatPosition - data.AAPosition;
                if (math.lengthsq(delta) < 0.001f) // CIVIC280: validate input before normalizesafe
                    continue;

                // Nose (+Z) points at the engaged threat (initial aim; chase refines it in Phase 2).
                quaternion rotation = quaternion.LookRotationSafe(math.normalizesafe(delta), math.up());

                buffer.Add(new InterceptorSpawnIntent
                {
                    SpawnPos = data.AAPosition + new float3(0f, LAUNCH_HEIGHT_OFFSET, 0f),
                    Rotation = rotation,
                    TargetPos = data.ThreatPosition,
                    ThreatIndex = data.ThreatIndex,
                    ThreatVersion = data.ThreatVersion,
                    Speed = INTERCEPTOR_SPEED,
                    Source = AAType.PatriotSAM,
                    PrefabIndex = interceptorPrefab.Index,
                    PrefabVersion = interceptorPrefab.Version,
                    PseudoSeed = (ushort)m_Random.NextUInt(0u, ushort.MaxValue),
                    SubMeshCount = subMeshCount,
                    LaunchFrame = launchFrame,
                    ThreatGeneration = generation,
                    IsBallistic = data.IsBallistic
                });
                spawned++;
            }

            if (spawned > 0 && Log.IsDebugEnabled)
                Log.Debug($"[Interceptor] queued {spawned} missile spawn intent(s)");
        }

        private int GetPrefabSubMeshCount(Entity prefabEntity)
        {
            if (prefabEntity == Entity.Null || !EntityManager.HasBuffer<SubMesh>(prefabEntity))
                return 1;
#pragma warning disable CIVIC051 // Spawn-time prefab buffer lookup; once per frame with queued shots, not a per-entity query loop.
            return math.max(1, EntityManager.GetBuffer<SubMesh>(prefabEntity, true).Length);
#pragma warning restore CIVIC051
        }
    }
}
