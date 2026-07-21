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
        /// launcher and appears to leave the cell instead of spawning inside the chassis.</summary>
        private const float LAUNCH_HEIGHT_OFFSET = 12f;

        /// <summary>Patriot-specific vertical launch offset (m): the canister block is taller than the
        /// Hawk's rail trailer, and at the shared 12 m the exhaust flame at the missile tail overlapped
        /// the vehicle on the first rendered frames. Tune by eye.</summary>
        private const float PATRIOT_LAUNCH_HEIGHT_OFFSET = 14.5f;

        /// <summary>Minimum launch elevation (deg above horizon). A Patriot leaves the cell in a near-
        /// vertical boost, not flat at the horizon — the movement system's capped turn (35°/tick) then
        /// arcs the missile from this climb onto the chase course. If the threat itself sits steeper,
        /// its real elevation wins. Tune by eye.</summary>
        private const float LAUNCH_PITCH_DEGREES = 70f;

        /// <summary>Asset-local offset from the AA prop's pivot to its launcher. The Hawk composite's
        /// pivot sits on the KrAZ truck while the launcher trailer stands beside it — without the
        /// offset missiles spawned over the truck roof. Rotated into world space by the AA's cached
        /// Transform rotation (AAFireEvent.AARotation). Tune by eye against the shipped .cok.</summary>
        private static readonly float3 HAWK_LAUNCHER_LOCAL_OFFSET = new float3(-7f, 0f, 0f);

        /// <summary>Sideways spacing (m) between neighbouring launch rails. A salvo alternates rails
        /// so simultaneous missiles do not stack in one point and read as a single "pack" during the
        /// engine's spawn-frame render hold.</summary>
        private const float RAIL_SPACING = 1.4f;
        private const int RAIL_COUNT = 3;

        private const int MAX_QUEUED_SPAWN_EVENTS = 32;

        /// <summary>Blittable copy of AAFireEvent (a record = reference type) for the NativeQueue.</summary>
        private struct InterceptorSpawnData
        {
            public float3 AAPosition;
            public quaternion AARotation;
            public float3 ThreatPosition;
            public int ThreatIndex;
            public int ThreatVersion;
            public bool IsBallistic;
            public AAType Source;
        }

        private NativeQueue<InterceptorSpawnData> m_SpawnQueue;
        private EntityQuery m_IntentQuery;
        private CivicPrefabInitSystem m_PrefabInit = null!;
        private SimulationSystem m_SimulationSystem = null!;
        private Unity.Mathematics.Random m_Random;
        // Rotating rail index for the per-shot spawn spread — visual only; resetting to rail 0 on
        // load is invisible, so deliberately not persisted.
        [System.NonSerialized] private int m_RailCounter;
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
                AARotation = evt.AARotation,
                ThreatPosition = evt.ThreatPosition,
                ThreatIndex = evt.ThreatIndex,
                ThreatVersion = evt.ThreatVersion,
                IsBallistic = evt.IsBallistic,
                Source = evt.AAType
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
                // Spawn over the LAUNCHER, not the asset pivot: rotate the asset-local launcher offset
                // into world space by the AA's cached rotation, plus a per-shot rail offset so salvo
                // missiles alternate rails instead of stacking in one point during the engine's
                // spawn-frame render hold. A default (all-zero) rotation means "unknown" (dev-tools
                // fire path) — spawn at the pivot then.
                float3 launchBase = data.AAPosition;
                if (math.lengthsq(data.AARotation.value) > 0.5f)
                {
                    float3 local = LauncherLocalOffset(data.Source);
                    local.x += ((m_RailCounter++ % RAIL_COUNT) - 1) * RAIL_SPACING;
                    launchBase += math.mul(data.AARotation, local);
                }

                float3 delta = data.ThreatPosition - launchBase;
                if (math.lengthsq(delta) < 0.001f) // CIVIC280: validate input before normalizesafe
                    continue;

                // Launch attitude: threat azimuth, but pitched up to at least LAUNCH_PITCH_DEGREES —
                // the boost climb. The movement system seeds its heading from this rotation and its
                // capped turn arcs the missile onto the chase course over the next ticks.
                float3 launchDir = ComputeLaunchDirection(delta);
                quaternion rotation = quaternion.LookRotationSafe(launchDir, math.up());

                buffer.Add(new InterceptorSpawnIntent
                {
                    SpawnPos = launchBase + new float3(0f, LaunchHeightOffset(data.Source), 0f),
                    Rotation = rotation,
                    TargetPos = data.ThreatPosition,
                    ThreatIndex = data.ThreatIndex,
                    ThreatVersion = data.ThreatVersion,
                    Speed = INTERCEPTOR_SPEED,
                    Source = data.Source,
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

        /// <summary>Asset-local pivot→launcher offset per AA type (the Patriot's canister block sits
        /// at its pivot — no offset needed).</summary>
        private static float3 LauncherLocalOffset(AAType type) =>
            type == AAType.HawkSAM ? HAWK_LAUNCHER_LOCAL_OFFSET : float3.zero;

        /// <summary>Vertical launch offset per AA type — see <see cref="PATRIOT_LAUNCH_HEIGHT_OFFSET"/>.</summary>
        private static float LaunchHeightOffset(AAType type) =>
            type == AAType.PatriotSAM ? PATRIOT_LAUNCH_HEIGHT_OFFSET : LAUNCH_HEIGHT_OFFSET;

        /// <summary>
        /// Direction the missile leaves the cell in: the threat's azimuth with the elevation raised to
        /// at least <see cref="LAUNCH_PITCH_DEGREES"/> (a steeper threat keeps its real elevation).
        /// Capped just below vertical so <c>LookRotationSafe</c> keeps a valid frame when the threat is
        /// directly overhead.
        /// </summary>
        private static float3 ComputeLaunchDirection(float3 toThreat)
        {
            const float MIN_PITCH_RADIANS = LAUNCH_PITCH_DEGREES * math.PI / 180f;
            const float MAX_PITCH_RADIANS = 85f * math.PI / 180f;

            float3 horizontal = new float3(toThreat.x, 0f, toThreat.z);
            float horizontalLen = math.length(horizontal);
            // Divide by a floored length (never < 1e-3) — no zero divide by construction. A threat
            // directly overhead (horizontalLen → 0) yields a ~zero azimuth, so the launch is vertical,
            // which is the right attitude for an overhead intercept anyway.
            float3 azimuth = horizontal / math.max(horizontalLen, 1e-3f);
            float elevation = math.atan2(toThreat.y, math.max(horizontalLen, 1e-3f));
            float pitch = math.clamp(elevation, MIN_PITCH_RADIANS, MAX_PITCH_RADIANS);
            return azimuth * math.cos(pitch) + new float3(0f, math.sin(pitch), 0f);
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
