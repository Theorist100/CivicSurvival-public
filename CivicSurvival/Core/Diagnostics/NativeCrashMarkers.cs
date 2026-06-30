namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Whitelisted native crash breadcrumb markers shared by Backtrace and telemetry recovery.
    /// </summary>
    public static class NativeCrashMarkers
    {
        public const string Unknown = "Runtime.UncleanShutdown.Unknown";
        // No DebrisFallJob marker: DebrisFallJob.Execute is index-free (Impacts.Add auto-resizes,
        // Ecb.AddComponent is deferred, CommandCount++ is scalar) — it cannot raise an AV. As a
        // resident "debris present" marker it only out-lived the finer TMS/AA windows and stole
        // crash attribution (s_LastMarker = most-recent entrant still in flight), so ~10 prod
        // crashes that really fired in another Burst job were misrecorded as DebrisFallJob.
        public const string ThreatMovementPipeline = "ThreatFlight.ThreatMovementSystem.Pipeline";
        public const string AirDefenseTargetingPipeline = "AirDefense.TargetingPipeline.Driver";

        /// <summary>
        /// Coarse always-on fallback: a city simulation is live (set while GameTimeSystem
        /// updates, cleared on teardown). The finer per-pipeline markers above enter later and
        /// override it while in flight (s_LastMarker = most-recent entrant); when they clear,
        /// this remains. A native crash during sim outside every fine window then records
        /// InSimulation instead of the blind UncleanShutdown.Unknown. Perf-neutral: one disk
        /// write per session (active-set dedup keeps the per-frame Mark write-free).
        /// </summary>
        public const string InSimulation = "Runtime.InSimulation";

        /// <summary>
        /// One-shot post-load render reinit phase (ThreatLoadRenderReinitSystem): restored threats get
        /// render-state + lifecycle tags re-applied on the first ModificationEnd after load — outside the
        /// sim-tick window, so without this marker a crash/hang here recovers as the blind Unknown.
        /// One disk write on entry + one on exit per load (one-shot, not per-frame).
        /// </summary>
        public const string LoadRenderReinit = "Runtime.LoadRenderReinit";

        /// <summary>
        /// DEBUG-only deliberate crash from the dev panel, used to validate that the
        /// breadcrumb → Backtrace → next-launch TelemetryCrashDetector pipeline
        /// actually records native crashes. Never reachable in a Release build.
        /// </summary>
        public const string DevForcedCrash = "DevTools.ThreatDebugSystem.DevForcedCrash";

        public static bool IsKnown(string marker)
            => marker == Unknown
            || marker == ThreatMovementPipeline
            || marker == AirDefenseTargetingPipeline
            || marker == InSimulation
            || marker == LoadRenderReinit
            || marker == DevForcedCrash;
    }
}
