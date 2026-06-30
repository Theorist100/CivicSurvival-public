using Game;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Logic;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Spawns AA tracer rounds in response to <c>AAFireEvent</c>. Runs in GameSimulation: does all
    /// the spawn-decision work — RNG, burst spread, per-AA config, muzzle-flash dedup — then creates
    /// one lightweight <see cref="Tracer"/> entity per round through the vanilla
    /// <c>EndFrameBarrier</c> ECB.
    ///
    /// A tracer entity carries ONLY the <see cref="Tracer"/> component — no Transform, no MeshBatch,
    /// no render archetype. It is drawn as a camera-facing emissive streak by <c>TracerRenderSystem</c>
    /// via <c>Graphics.RenderMesh</c>, not by the BRG mesh pipeline. Because the entity is not a
    /// render chunk, creating it from GameSimulation is safe (no render-chunk migration into a live
    /// batch pass — the failure mode the old BRG split fought) and needs no off-barrier intent split
    /// or render-completion gate.
    ///
    /// Pause-safe by construction: AA does not fire in pause and GameSimulation does not tick in
    /// pause, so no tracer is ever spawned while paused.
    ///
    /// Visual-only system — no gameplay impact.
    /// </summary>
    [ActIndependent]
    public partial class TracerSpawnSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("TracerSpawnSystem");

        // Tracer speeds by AA type (m/s)
        private const float HERITAGE_BOFORS_SPEED = 800f;
        private const float BOFORS_40MM_SPEED = 880f;
        private const float GEPARD_SPEED = 1100f;
        private const float PATRIOT_SPEED = 1700f;

        // Tracer lifetimes (seconds)
        private const float AUTOCANNON_LIFETIME = 0.8f;
        private const float GEPARD_LIFETIME = 0.7f;
        private const float PATRIOT_LIFETIME = 1.0f;

        // Seconds between consecutive rounds of one AA burst, so the burst leaves the barrel as a
        // sequential stream (rate-of-fire feel) instead of a single simultaneous fan. ~0.05s ≈ a fast
        // autocannon cadence. Visual-only — tune freely.
        private const float BURST_GAP = 0.05f;

        // Spread angles (degrees → radians in config)
        private const float AUTOCANNON_SPREAD_DEG = 2f;
        private const float GEPARD_SPREAD_DEG = 1.5f;

        // Streak colours (linear RGBA). Orange-red, like real Western tracer rounds (yellow reads as
        // a laser); distinguishable per gun but all in the same hot orange→red family.
        private static readonly float4 AUTOCANNON_COLOR = new(1f, 0.42f, 0.10f, 1f);  // hot orange
        private static readonly float4 GEPARD_COLOR = new(1f, 0.35f, 0.08f, 1f);      // deep orange
        private static readonly float4 PATRIOT_COLOR = new(1f, 0.28f, 0.06f, 1f);     // orange-red

        /// <summary>
        /// Blittable value type for NativeQueue (AAFireEvent is a record = reference type).
        /// </summary>
        private struct TracerSpawnData
        {
            public float3 AAPosition;
            public float3 ThreatPosition;
            public AAType AAType;
            public int AAEntityIndex;
        }

        private struct TracerConfig
        {
            public int BurstCount;
            public float Speed;
            public float SpreadRadians;
            public float Lifetime;
            public float4 Color;
        }

        private NativeQueue<TracerSpawnData> m_SpawnQueue;
        private Unity.Mathematics.Random m_Random;
        private VanillaVfxSystem? m_VanillaVfx;
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private const int MAX_QUEUED_SPAWN_EVENTS = 64;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SpawnQueue = new NativeQueue<TracerSpawnData>(Allocator.Persistent);
            // R9-L16: not serialized — visual-only (tracer spread/offset), no gameplay impact
            m_Random = new Unity.Mathematics.Random((uint)(System.Environment.TickCount ^ 0xAA01));

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

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
            // Missile launchers (Patriot) spawn a guided interceptor via InterceptorSpawnSystem and
            // draw NO gun tracers; only autocannons (Bofors/Gepard/Heritage) fire tracer streaks.
            // Weapon class is owned by AATypeWeapon — the inverse gate lives in InterceptorSpawnSystem.
            if (evt.AAType.FiresInterceptorMissile())
                return;

            while (m_SpawnQueue.Count >= MAX_QUEUED_SPAWN_EVENTS && m_SpawnQueue.TryDequeue(out _))
            {
                // Drop oldest unresolved visual events only when the bounded retry queue is full.
            }

            m_SpawnQueue.Enqueue(new TracerSpawnData
            {
                AAPosition = evt.AAPosition,
                ThreatPosition = evt.ThreatPosition,
                AAType = evt.AAType,
                AAEntityIndex = evt.AAEntityIndex
            });
        }

        protected override void OnUpdateImpl()
        {
            if (m_SpawnQueue.Count == 0)
                return;

            // Lazy resolve — VanillaVfxSystem may not exist in stripped/test worlds; cache on first success.
            if (m_VanillaVfx == null)
                m_VanillaVfx = World.GetExistingSystemManaged<VanillaVfxSystem>();

            // Tracer entities are created through the vanilla EndFrameBarrier: a Tracer-only entity is
            // not a render chunk, so the create cannot migrate a live BRG batch (the reason the old
            // mesh path used an off-barrier Modification4 split). The entity becomes visible to
            // TracerRenderSystem next frame (1-frame latency, invisible for a ~0.7-1.0s round).
            // Allocated lazily so a frame whose queued events all degenerate (zero-length delta)
            // never creates an empty command buffer (CIVIC486).
            EntityCommandBuffer? ecb = null;

            // Deduplicate muzzle flash — same AA may fire-event multiple times per frame.
            // Key by AA entity index (int) — float3 equality is unreliable (CIVIC282).
            var muzzleFlashedAAIndices = new NativeHashSet<int>(4, Allocator.Temp);

            int totalSpawned = 0;

            while (m_SpawnQueue.TryDequeue(out var data))
            {
                var config = GetTracerConfig(data.AAType);
                float3 delta = data.ThreatPosition - data.AAPosition;
                if (math.lengthsq(delta) < 0.001f) // CIVIC280: check INPUT before normalizesafe, not output
                    continue;

                float3 direction = math.normalizesafe(delta);

                // Muzzle flash at AA barrel (once per AA per frame — no duplicates)
                if (!muzzleFlashedAAIndices.Contains(data.AAEntityIndex))
                {
                    muzzleFlashedAAIndices.Add(data.AAEntityIndex);
                    m_VanillaVfx?.RequestExplosion(data.AAPosition, ExplosionType.MuzzleFlash);
                }

                for (int i = 0; i < config.BurstCount; i++)
                {
                    float3 spreadDir = ApplySpread(direction, config.SpreadRadians);
                    ecb ??= m_EndFrameBarrier.CreateCommandBuffer();
                    // Stagger each round's launch by its burst index so the burst streams out of the
                    // barrel sequentially instead of as a simultaneous fan.
                    SpawnTracer(ecb.Value, data.AAPosition, spreadDir, config, i * BURST_GAP);
                    totalSpawned++;
                }
            }

            if (muzzleFlashedAAIndices.IsCreated) muzzleFlashedAAIndices.Dispose();

            if (totalSpawned > 0 && Log.IsDebugEnabled)
                Log.Debug($"[TRACER] Spawned {totalSpawned} tracer round(s)");
        }

        private static void SpawnTracer(EntityCommandBuffer ecb, float3 origin, float3 direction, in TracerConfig config, float fireDelay)
        {
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new Tracer
            {
                Origin = origin,
                Direction = direction,
                Speed = config.Speed,
                Lifetime = config.Lifetime,
                // Seed with the launch delay added so the round stays invisible until its delay elapses,
                // then flies its full Lifetime (render derives launch via Lifetime - RemainingLife).
                RemainingLife = config.Lifetime + fireDelay,
                FireDelay = fireDelay,
                Color = config.Color
            });
#if UNITY_EDITOR
            ecb.SetName(entity, "Tracer");
#endif
        }

        private float3 ApplySpread(float3 direction, float spreadRadians)
        {
            if (spreadRadians <= 0f)
                return direction;

            float yaw = m_Random.NextFloat(-spreadRadians, spreadRadians);
            float pitch = m_Random.NextFloat(-spreadRadians, spreadRadians);

            // FIX S20-#14: Fallback axis when direction is near-vertical (cross product ≈ zero)
            float3 pitchAxis = math.cross(direction, math.up());
            if (math.lengthsq(pitchAxis) < 0.001f)
                pitchAxis = math.right();
            else
                pitchAxis = math.normalize(pitchAxis); // cross product has magnitude sin(θ), AxisAngle needs unit vector

            quaternion spreadRot = math.mul(
                quaternion.AxisAngle(math.up(), yaw),
                quaternion.AxisAngle(pitchAxis, pitch)
            );

            return math.normalizesafe(math.mul(spreadRot, direction));
        }

        private static TracerConfig GetTracerConfig(AAType aaType)
        {
            // One ForType call per spawn (cached here), reused by whichever arm runs.
            int burst = AAParams.ForType(BalanceConfig.Current, aaType).BurstRounds;
            return aaType switch
            {
                AAType.HeritageBofors => new TracerConfig
                {
                    BurstCount = burst,
                    Speed = HERITAGE_BOFORS_SPEED,
                    SpreadRadians = math.radians(AUTOCANNON_SPREAD_DEG),
                    Lifetime = AUTOCANNON_LIFETIME,
                    Color = AUTOCANNON_COLOR
                },
                AAType.Bofors40mm => new TracerConfig
                {
                    BurstCount = burst,
                    Speed = BOFORS_40MM_SPEED,
                    SpreadRadians = math.radians(AUTOCANNON_SPREAD_DEG),
                    Lifetime = AUTOCANNON_LIFETIME,
                    Color = AUTOCANNON_COLOR
                },
                AAType.Gepard => new TracerConfig
                {
                    BurstCount = burst,
                    Speed = GEPARD_SPEED,
                    SpreadRadians = math.radians(GEPARD_SPREAD_DEG),
                    Lifetime = GEPARD_LIFETIME,
                    Color = GEPARD_COLOR
                },
                AAType.PatriotSAM => new TracerConfig
                {
                    BurstCount = burst,
                    Speed = PATRIOT_SPEED,
                    SpreadRadians = 0f,
                    Lifetime = PATRIOT_LIFETIME,
                    Color = PATRIOT_COLOR
                },
                _ => new TracerConfig
                {
                    BurstCount = burst,
                    Speed = HERITAGE_BOFORS_SPEED,
                    SpreadRadians = math.radians(AUTOCANNON_SPREAD_DEG),
                    Lifetime = AUTOCANNON_LIFETIME,
                    Color = AUTOCANNON_COLOR
                }
            };
        }
    }
}
