using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Single source of truth for the act-generation ("which act am I from") stamp
    /// that rides the transient threat → arrival → impact → debris pipeline (C-5 root
    /// fix). One shared mutable <see cref="Current"/>: stamp-at-spawn and
    /// compare-at-impact read the same object, so inter-consumer desync is
    /// structurally impossible (no per-consumer reset to keep in lockstep) and there
    /// is no ECS singleton read → no sync point.
    ///
    /// Registered as a process-lifetime service in <c>Mod.OnLoad</c> (before any
    /// system <c>OnCreate</c>) so the init-order hazard is eliminated by
    /// construction. <see cref="ServiceRegistry"/> survives a same-session save→load
    /// (disposed only in <c>Mod.OnDispose</c>), so the single instance and every
    /// cached consumer ref stay valid across load.
    ///
    /// Sole writer: <c>ScenarioStateMachine</c> — advances on a real act transition
    /// and on every load (load IS an epoch boundary: every pre-load transient
    /// becomes stale by construction regardless of its value).
    ///
    /// <see cref="Unstamped"/> (0) is INVALID, never a normal value: a forgotten
    /// stamp / lingering zombie surfaces as a loud, counted, always-dropped impact
    /// instead of "sometimes a normal impact".
    /// </summary>
    [InfrastructureService]
    public sealed class ActEpochClock
    {
        /// <summary>Sentinel for a never-stamped transient. Invalid — fail loud.</summary>
        public const int Unstamped = 0;

        /// <summary>Current act-generation. Valid baseline = 1 (0 is invalid).</summary>
        public int Current { get; private set; } = 1;

        /// <summary>Advance on a real act change (ScenarioStateMachine.TransitionToAct).</summary>
        public int AdvanceForActTransition() => ++Current;

        /// <summary>
        /// Advance on every load path. The load itself is an epoch boundary —
        /// advancing (not resetting to a constant) makes every preserved pre-load
        /// transient stale by construction, regardless of its stamped value.
        /// Consequence: <see cref="Current"/> drifts across loads — diagnostics and
        /// tests must read <see cref="Current"/>, never hard-code a number.
        /// </summary>
        public int AdvanceForLoad() => ++Current;
    }
}
