using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Effects
{
    /// <summary>
    /// Late-phase driver for <see cref="VanillaVfxSystem"/>'s owner-attach queue.
    ///
    /// The exhaust controllers (<c>InterceptorExhaustSystem</c>,
    /// <c>ThreatSpawnSystem.EnsureBallisticExhaustAttached</c>) collect their re-attach
    /// requests in GameSimulation (render-frame-gated) and push them into
    /// <see cref="VanillaVfxSystem.EnqueueOwnerAttach"/>. This system drains the whole queue
    /// ONCE per frame in <c>SystemUpdatePhase.CompleteRendering</c>, which runs AFTER this
    /// frame's PreCulling (EffectControlSystem schedules the city effect graph) and Rendering
    /// (EffectTransformSystem reads/writes <c>m_EnabledData</c>) — both in MainLoop, before
    /// GameSimulation (LateUpdate). By CompleteRendering the engine's effect jobs for the frame
    /// have had the entire GameSimulation phase to finish, so
    /// <c>GetEnabledData(false).Complete()</c> inside the batch attach is a (near-)noop instead
    /// of the real ~800-1060ms city-graph wait it was when drained from GameSimulation
    /// (VanillaProfiler 2026-06-25). New records appear next frame (EffectTransformSystem already
    /// ran), a 1-frame latency that is invisible — the seed position is overwritten by the
    /// owner's InterpolatedTransform anyway.
    ///
    /// Pause (Axiom 14): CompleteRendering ticks in pause. The GameSimulation producers do NOT
    /// run in pause, so the queue is already empty there — but gate on
    /// <c>selectedSpeed != 0f</c> explicitly so a frozen frame can never re-drain, matching the
    /// pause semantics of the GameSimulation-resident controllers it replaces.
    /// </summary>
    [ActIndependent]
    public partial class VanillaVfxLateAttachSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("VanillaVfxLateAttach");

        private VanillaVfxSystem m_Vfx = null!;
        private SimulationSystem m_SimulationSystem = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Same-domain sibling resolution: this driver and VanillaVfxSystem both register
            // inside EffectsDomain; there is no feature-gate path where one exists without the
            // other. CIVIC400 false positive.
#pragma warning disable CIVIC400
            m_Vfx = World.GetOrCreateSystemManaged<VanillaVfxSystem>();
#pragma warning restore CIVIC400
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            Log.Info("Created (CompleteRendering owner-attach drain)");
        }

        protected override void OnUpdateImpl()
        {
            // PERF-LOCK: pause-gate — CompleteRendering ticks in pause but the GameSimulation
            // producers do not, so a frozen frame must not re-drain the (stale-but-empty) queue.
            // selectedSpeed is the vanilla pause signal (SimulationSystem.OnUpdate:221): 0f when
            // paused, ≥1f when playing, never negative — so <= 0f means paused (relational compare
            // avoids S1244's float-equality flag).
            if (m_SimulationSystem.selectedSpeed <= 0f)
                return;

            m_Vfx.FlushOwnerAttachQueue();
        }
    }
}
