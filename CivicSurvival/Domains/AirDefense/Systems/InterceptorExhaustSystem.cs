using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Objects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Keeps the owner-attached engine exhaust (<c>FireMovingMediumVFX</c>) alive on every airborne
    /// interceptor. Runs in GameSimulation EVERY frame and re-asserts the record once per
    /// <see cref="REATTACH_RENDER_INTERVAL"/> render frames — mirror of <c>ThreatSpawnSystem.EnsureBallisticExhaustAttached</c>.
    ///
    /// <para>Deliberately NOT folded into <c>InterceptorMovementSystem</c>: that system ticks once per
    /// 16 frames (OIS cadence), which is too slow to re-inject a record <c>EffectControlSystem</c> may
    /// drop on a re-evaluation (~every 6 frames) → the flame would flicker. The exhaust controller
    /// therefore needs its own per-frame cadence.</para>
    ///
    /// <c>VanillaVfxSystem.TryAttachEffect</c> validates the existing record and re-injects only when
    /// it is missing/stale; the engine does the rest (<c>EffectTransformSystem</c> follows the missile's
    /// InterpolatedTransform every frame, reading the nozzle offset from element 0 of the AIM120 prefab's
    /// Effect buffer, bound by <c>CivicPrefabInitSystem.TryBindInterceptorExhaust</c>). Cosmetic only —
    /// no gameplay state, PvP-safe. Pause-safe: GameSimulation does not tick in pause.
    /// </summary>
    [ActIndependent]
    public partial class InterceptorExhaustSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("InterceptorExhaustSystem");

        // Re-assert the exhaust on a RENDER cadence (Time.frameCount), not sim frameIndex. Vanilla
        // EffectControlSystem runs once per render frame (PreCulling) and drops the injected record,
        // so the reattach only needs to track render frames. Gating on sim frameIndex scaled the
        // EnabledData drain with game speed — under a wave Sim runs 84-96 ticks/s, so a %6-sim gate
        // fired ~15×/s; a render gate fires at the (much lower, wave-throttled) render rate instead.
        // PERF-LOCK: render-frame gate — do NOT revert to sim frameIndex; that re-multiplies the
        // city-wide EnabledData drain by game speed (was ~800-1060ms under a wave, VanillaProfiler
        // 2026-06-25). Interval 3 keeps the flame within EffectControlSystem's ~6-render drop window.
        private const int REATTACH_RENDER_INTERVAL = 3;

        private EntityQuery m_InterceptorQuery;
        private VanillaVfxSystem? m_VanillaVfx;
        private EffectCacheSystem? m_EffectCache;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_InterceptorQuery = GetEntityQuery(
                ComponentType.ReadOnly<InterceptorTag>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>());
            RequireForUpdate(m_InterceptorQuery);
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            if (UnityEngine.Time.frameCount % REATTACH_RENDER_INTERVAL != 0)
                return;

            m_VanillaVfx ??= World.GetExistingSystemManaged<VanillaVfxSystem>();
            m_EffectCache ??= World.GetExistingSystemManaged<EffectCacheSystem>();
            if (m_VanillaVfx == null || !m_VanillaVfx.IsReady || m_EffectCache == null
                || !m_EffectCache.TryGetEffect(EffectNames.FIRE_MOVING_MEDIUM_VFX, out Entity effectPrefab))
                return;

            // Collect targets first — the late-phase drain does structural buffer writes (RemoveAt/Add)
            // on the owner, illegal inside a live SystemAPI.Query iteration. Element 0 of the AIM120
            // prefab's Effect buffer. Best-effort — a failed attach retries next cycle.
            // PERF-LOCK: seed read from the mod-only Interceptor.CurrentPosition (written by
            // InterceptorMovementSystem), NOT Game.Objects.Transform — a Transform RO query here drains
            // the city-wide transform job chain (the sync point that dominated this controller under a
            // wave). Mirror of ballistics reading ThreatPosition instead of Transform. Do NOT switch the
            // seed back to Transform.
            // DIAG: split the controller's two phases so the PERF profiler attributes any spike.
            // Collect iterates the Interceptor chunks (RO) — if InterceptorMovementSystem's render job
            // (Transform/Moving RW on these chunks, out of Dependency) forces a main-thread completion
            // here, it lands on SP:InterceptorExhaust.Collect; a pure-copy cost lands on .Enqueue.
            // Measure() is gated by the profiler's Enabled switch (records at Level.Info, no Debug
            // distortion), so these are honest sync-point numbers, not a Debug artifact.
            var requests = new NativeList<VfxAttachRequest>(Allocator.Temp);
            using (PerformanceProfiler.Measure("SP:InterceptorExhaust.Collect"))
            {
                foreach (var (interceptorRef, entity) in
                         SystemAPI.Query<RefRO<Interceptor>>()
                             .WithAll<InterceptorTag>()
                             .WithNone<Deleted>()
                             .WithEntityAccess())
                {
                    requests.Add(new VfxAttachRequest(entity, effectPrefab, 0, interceptorRef.ValueRO.CurrentPosition));
                }
            }

            // Enqueue for the deferred late-phase drain (VanillaVfxLateAttachSystem,
            // CompleteRendering). The EnabledData deps.Complete() does NOT run here in
            // GameSimulation — it would wait on this frame's in-flight city effect graph
            // (EffectControlSystem schedules in PreCulling/MainLoop, EffectTransformSystem writes in
            // Rendering/MainLoop, both before GameSimulation/LateUpdate). Draining in
            // CompleteRendering (after GameSimulation) makes that fence a noop.
            using (PerformanceProfiler.Measure("SP:InterceptorExhaust.Enqueue"))
            {
                m_VanillaVfx.EnqueueOwnerAttach(requests);
            }

            requests.Dispose();
        }
    }
}
