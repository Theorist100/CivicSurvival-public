namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Coarse process lifecycle phase recorded by the crash heartbeat (<see cref="CrashContextProvider"/>)
    /// so a recovered abnormal-shutdown breadcrumb can be classified by <c>phase × fault-code</c> on the
    /// dashboard — e.g. a <c>Loading</c>-phase <c>0x0517A7ED</c> ANR is a legitimate long sync op (the
    /// Backtrace watchdog false-fires on synchronous city load), NOT an in-game freeze, whereas the same
    /// sentinel under <see cref="ActiveSim"/> is a genuine in-game freeze.
    ///
    /// <para><b>Decompile-verified phase boundaries (CS2 <c>SimulationSystem.OnUpdate</c>).</b> The vanilla
    /// <c>GameSimulation</c> phase ticks ONLY when <c>selectedSpeed != 0</c> (<c>:221</c> → <c>num=0</c> at
    /// pause, <c>:273</c> gates the phase on <c>num != 0</c>). Our <c>GameTimeSystem</c> (which stamps the
    /// fine <c>InSimulation</c> marker) and <c>TelemetryService</c> both live in <c>GameSimulation</c>, so
    /// NEITHER runs while the sim is paused. <c>OnGameLoadingComplete</c> fires (and <c>pausedAfterLoading</c>
    /// is applied, <c>:213</c>) BEFORE the first sim tick — so the instant load finishes is NOT yet "the sim
    /// is running". <see cref="ActiveSim"/> is therefore stamped at the first real <c>GameTimeSystem</c> tick
    /// (next to the <c>InSimulation</c> marker), NOT at load-complete; the window between them — including a
    /// <c>pausedAfterLoading</c> city that may sit on the start-pause indefinitely — is <see cref="Loaded"/>.
    /// A <c>0x0517A7ED</c> ANR under <see cref="Loaded"/> is a freeze before the sim ever ticked (load-tail
    /// heavy frame or paused-after-load), NOT a gameplay-simulation freeze — same low-likelihood class as
    /// <see cref="Loading"/>/<see cref="Saving"/>.</para>
    ///
    /// Whitelisted enum — no free text — so it is GDPR-safe to ship raw in telemetry. The mod records the
    /// phase as a RAW signal and asserts no verdict (see <c>Docs/Diagnostics/AbnormalShutdownDetection.md</c>);
    /// the crash/not-crash reading is formed on the dashboard.
    /// </summary>
    public enum LifecyclePhase
    {
        Unknown = 0,
        Boot,
        Menu,
        Loading,

        /// <summary>
        /// World is ready (load finished) but the simulation has not ticked yet: the
        /// <c>OnGameLoadingComplete → first GameSimulation tick</c> window, and — decisively — a
        /// <c>pausedAfterLoading</c> city, where <c>GameSimulation</c> never ticks until the player
        /// unpauses, so this phase can persist for as long as the start-pause is held. A fault here is
        /// NOT an in-game freeze (the sim is not running); it reads as low-likelihood like Loading/Saving.
        /// </summary>
        Loaded,
        ActiveSim,
        Saving,
        ShuttingDown,
        Exited,
    }
}
