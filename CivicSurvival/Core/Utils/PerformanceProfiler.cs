using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Lightweight performance profiler for diagnosing lag sources.
    ///
    /// Usage:
    ///   using (PerformanceProfiler.Measure("MySystem.OnUpdate"))
    ///   {
    ///       // code to measure
    ///   }
    ///
    /// Output: Writes to separate PERF.log file (not main log).
    /// Control: Set PerformanceProfiler.Enabled = false to disable.
    /// </summary>
    public static class PerformanceProfiler
    {
        internal const double MS_PER_SECOND = 1000.0;
        internal const float REPORT_INTERVAL_SECONDS = 5.0f;
        internal const double SLOW_THRESHOLD_MS = 5.0;

        private const int CLEAN_COLUMN_WIDTH = 10;
        private const int STATE_LABEL_COLUMN_WIDTH = 20;
        private const int STATE_SUMMARY_INTERVAL = 6; // every 6th report (~30s)
        private const int MAX_STATE_STATS = 64;
        private const string TOGGLE_PREFIX = "TOGGLE ";

        private static readonly Dictionary<string, ProfileData> s_Data = new();
        private static readonly Dictionary<string, int> s_JobEntityCounts = new();
        private static readonly Dictionary<string, AllocationData> s_AllocationData = new();
        private static readonly object s_Lock = new();

        // Reused per-Report snapshot buffers — Report() runs main-thread only (called
        // from OnFrame on the Rendering phase), so static reuse is safe and removes
        // three List/Dictionary allocations per 5s reporting cycle.
        private static readonly List<KeyValuePair<string, ProfileData>> s_SnapshotBuffer = new();
        private static readonly Dictionary<string, int> s_JobEntityBuffer = new();
        private static readonly List<KeyValuePair<string, AllocationData>> s_AllocationBuffer = new();
        private static float s_ReportTimer;
        private static int s_ReportCount;

        // Render FPS tracking (called from UpdateSystemProfilerPatch on Rendering phase)
        private static long s_LastFrameTicks;
        private static long s_FrameTimeSum;
        private static long s_FrameTimeMax;
        private static int s_FrameTimeSamples;

        // Simulation tick tracking (called from PerformanceProfilerSystem in SimulationSystemGroup)
        private static int s_SimTickCount;

        // Total measured time (for SimulationProfilerPatch comparison)
        private static long s_TotalMeasuredTicks;

        // Nesting depth: only add to s_TotalMeasuredTicks at depth 0 to avoid double-counting
        // (e.g., ThreatMovement.OnUpdate wraps CollectInputs + ObstacleCheck + MovementComplete)
        [ThreadStatic] private static int s_NestingDepth;

        // Auto-statistics: previous report snapshot (for DELTA after toggle)
        private static double s_PrevAvgFps;
        private static double s_PrevAvgFrameMs;
        private static double s_PrevSimTicksPerSec;
        private static volatile bool s_HasPreviousAverages;
        private static volatile bool s_StateChanged;

        // Domain state tracking (from LogMarker TOGGLE events)
        private static readonly Dictionary<string, bool> s_DomainStates = new();
        private static string s_CurrentStateLabel = "";

        // Per-state rolling averages
        private sealed class StateAccumulator
        {
            public double FpsSum;
            public double FrameMsSum;
            public double SimTicksSum;
            public int SampleCount;
        }
        private static readonly Dictionary<string, StateAccumulator> s_StateStats = new();

        /// <summary>
        /// Enable/disable profiling. Disabled = zero overhead.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Log slow operations immediately (can be noisy during attacks).
        /// </summary>
        public static bool LogSlowImmediately { get; set; } = false;

        public static bool ABTestRunning => PerfABTest.Running;

        // ═══════ Data Types ═══════

        internal sealed class ProfileData
        {
            public long TotalTicks;
            public long TotalTicksNoGC;  // sum excluding GC-affected calls
            public int CallCount;
            public long MaxTicks;
            public int SlowCount;  // calls > threshold
            public int GcHitCount;     // calls where Gen2 GC happened during measurement
            public long MaxTicksNoGC;  // max ticks excluding GC-affected calls

            public double TotalMs => TotalTicks * MS_PER_SECOND / Stopwatch.Frequency;
            public double CleanTotalMs => TotalTicksNoGC * MS_PER_SECOND / Stopwatch.Frequency;
            public double AvgMs => CallCount > 0 ? TotalMs / CallCount : 0;
            public double MaxMs => MaxTicks * MS_PER_SECOND / Stopwatch.Frequency;
            public double MaxMsNoGC => MaxTicksNoGC * MS_PER_SECOND / Stopwatch.Frequency;
        }

        internal sealed class AllocationData
        {
            public long TotalBytes;
            public int CallCount;
        }

        // ═══════ Lifecycle ═══════

        // Set by Initialize(), cleared by Shutdown(). OnFrame early-returns when
        // false so render-frame Harmony patch does not spam main log via fallback
        // path when profiler is not active (e.g. main menu before city load).
        private static volatile bool s_Initialized;

        /// <summary>
        /// True between Initialize() and Shutdown(). Allows callers (e.g. OnFrame)
        /// to early-return when the profiler is dormant.
        /// </summary>
        public static bool IsInitialized => s_Initialized;

        /// <summary>
        /// Initialize profiler with log path. Call from Mod.OnLoad().
        /// </summary>
        public static void Initialize(string logDirectory)
        {
            PerfLogWriter.Initialize(logDirectory, SLOW_THRESHOLD_MS, REPORT_INTERVAL_SECONDS);
            ResetAllStateLocked();
            s_Initialized = true;
        }

        /// <summary>
        /// Cleanup on shutdown.
        /// </summary>
        public static void Shutdown()
        {
#pragma warning disable CIVIC052 // Cleanup: can't log during dispose
            try
            {
                s_Initialized = false;
                PerfABTest.ResetWithoutCallbacks();
                ResetAllStateLocked();
                PerfLogWriter.Shutdown();
            }
            catch { /* ignore */ }
#pragma warning restore CIVIC052
        }

        private static void ResetAllStateLocked()
        {
            lock (s_Lock)
            {
                s_Data.Clear();
                s_JobEntityCounts.Clear();
                s_AllocationData.Clear();
                s_ReportTimer = 0f;
                s_ReportCount = 0;
                s_LastFrameTicks = 0;
                s_FrameTimeSum = 0;
                s_FrameTimeMax = 0;
                s_FrameTimeSamples = 0;
                s_SimTickCount = 0;
                s_TotalMeasuredTicks = 0;
                s_PrevAvgFps = 0;
                s_PrevAvgFrameMs = 0;
                s_PrevSimTicksPerSec = 0;
                s_HasPreviousAverages = false;
                s_StateChanged = false;
                s_DomainStates.Clear();
                s_CurrentStateLabel = "";
                s_StateStats.Clear();
            }
        }

        // ═══════ Public API ═══════

        /// <summary>
        /// Write a timestamped marker to PERF.log (for toggle events, etc.).
        /// </summary>
        public static void LogMarker(string message)
        {
            // Profiler dormant (menu / pre-Initialize / post-Shutdown): PerfLogWriter
            // is null and WriteMarker falls back to the main log, spamming it with
            // "TOGGLE …" entries. Mirror OnFrame's IsInitialized gate.
            if (!s_Initialized) return;
            if (string.IsNullOrEmpty(message))
                return;

            PerfLogWriter.WriteMarker(message);

            if (message.StartsWith(TOGGLE_PREFIX, StringComparison.Ordinal))
            {
                ParseToggle(message);
            }
        }

        /// <summary>
        /// Start automated A/B test: alternates domain ON/OFF every (reportsPerPhase * 5s),
        /// collects FPS/frame/sim stats per phase, outputs comparison with stddev after all cycles.
        /// </summary>
        /// <param name="domainKey">Domain key for logging (e.g., "d:threats")</param>
        /// <param name="cycles">Number of ON+OFF cycles (3 = 3min total)</param>
        /// <param name="reportsPerPhase">Reports per phase (6 = 30s per ON/OFF phase)</param>
        /// <param name="toggleAction">Callback to toggle domain ON (true) / OFF (false)</param>
        public static void StartABTest(string domainKey, int cycles, int reportsPerPhase, Action<bool> toggleAction)
        {
            if (!s_Initialized) return;
            PerfABTest.Start(domainKey, cycles, reportsPerPhase, toggleAction, REPORT_INTERVAL_SECONDS);
        }

        /// <summary>
        /// Cancel running A/B test. Restores domain to ON state.
        /// </summary>
        public static void StopABTest()
        {
            if (!s_Initialized) return;
            PerfABTest.Stop();
        }

        /// <summary>
        /// Start sequential A/B tests: tests each toggle one after another automatically.
        /// Press 1 button → all toggles tested → results in PERF.log.
        /// </summary>
        public static void StartABTestSequence(string[] keys, Action<bool>[] toggles, int cycles, int reportsPerPhase)
        {
            if (!s_Initialized) return;
            PerfABTest.StartSequence(keys, toggles, cycles, reportsPerPhase, REPORT_INTERVAL_SECONDS);
        }

        /// <summary>
        /// Returns progress text for UI, e.g. "ON 4/6 cycle 2/3 ~1:25"
        /// </summary>
        public static string GetABProgressText()
        {
            return PerfABTest.GetProgressText();
        }

        /// <summary>
        /// Measure execution time of a code block.
        /// Returns concrete struct type to avoid boxing (zero-alloc).
        /// </summary>
        public static MeasureScope Measure(string name)
        {
            // Gated by the profiler's own Enabled switch — independent of log level so
            // it can record at Level.Info, where sync-point numbers are honest. (At
            // Debug the extra hot-path logging slows the main thread and masks
            // worker-bound sync, so sync verdicts must be taken at Info.) Enabled=false
            // → no-op default scope, zero cost: that is the beta / shipping state.
            return Enabled ? new MeasureScope(name) : default;
        }

        /// <summary>
        /// Debug-only measurement. Returns no-op scope when not in debug mode.
        /// Use for investigation probes that should not run in production.
        /// </summary>
        public static MeasureScope MeasureDebug(string name)
        {
            return s_DebugEnabled ? new MeasureScope(name) : default;
        }

        private static volatile bool s_DebugEnabled;
        /// <summary>Set by Mod.cs when Log.IsDebugEnabled. Controls MeasureDebug probes.</summary>
        public static void SetDebugMode(bool enabled) => s_DebugEnabled = enabled;

        /// <summary>
        /// True when debug profiling is active (Log.IsDebugEnabled). Read by Harmony
        /// patches that record per-system Update timing so they no-op at Level.Info
        /// (zero observer overhead in production). FPS/memory/leak tracking via OnFrame
        /// is independent of this flag and always runs.
        /// </summary>
        public static bool IsDebugMode => s_DebugEnabled;

        /// <summary>
        /// Get total time measured across all systems since last reset.
        /// Used by SimulationProfilerPatch to compare with total frame time.
        /// </summary>
        public static float GetTotalMeasuredMs()
        {
            lock (s_Lock)
            {
                return (float)(s_TotalMeasuredTicks * MS_PER_SECOND / Stopwatch.Frequency);
            }
        }

        /// <summary>
        /// Reset total measured time counter.
        /// Called by SimulationProfilerPatch after each report.
        /// </summary>
        public static void ResetTotalMeasured()
        {
            lock (s_Lock)
            {
                s_TotalMeasuredTicks = 0;
            }
        }

        /// <summary>
        /// Store pre-formatted UI React profiling report from JS.
        /// Called from MainMenuShellUISystem trigger handler (UiProfileReport
        /// is menu-safe — JS can send reports independently of city load).
        /// </summary>
        public static void RecordUIReactReport(string report)
        {
            PerfReportSections.SetUIReactReport(report);
        }

        /// <summary>
        /// Reset memory baseline. Call after save load to track fresh deltas.
        /// </summary>
        public static void ResetMemoryBaseline()
        {
            PerfReportSections.ResetMemoryBaseline();
        }

        // ═══════ Frame / Tick Tracking ═══════

        /// <summary>
        /// Call once per RENDER frame to track actual FPS and trigger reports.
        /// Called from UpdateSystemProfilerPatch on Rendering phase.
        /// </summary>
        public static void OnFrame()
        {
            // Early-return when profiler is not initialized. Render-frame Harmony
            // patch fires OnFrame unconditionally; without this guard Report() at
            // the 5s mark would fall back to main log via PerfLogWriter (writer is
            // null), spamming menu sessions where profiler is intentionally dormant.
            if (!s_Initialized) return;

            // Note: Enabled controls per-binding profiling overhead only.
            // OnFrame (PERF.log, FPS, memory) always runs — see UpdateSystemProfilerPatch comment.

            // FPS tracking — HIGH-1 FIX: protect FPS fields with s_Lock
            // On .NET 4.8 (32-bit target), long reads/writes are NOT atomic
            long now = Stopwatch.GetTimestamp();
            bool shouldReport = false;

            lock (s_Lock)
            {
                if (s_LastFrameTicks > 0)
                {
                    long delta = now - s_LastFrameTicks;
                    s_FrameTimeSum += delta;
                    s_FrameTimeSamples++;
                    if (delta > s_FrameTimeMax) s_FrameTimeMax = delta;

                    // Time-based report interval
                    float frameDeltaSec = (float)(delta * 1.0 / Stopwatch.Frequency);
                    s_ReportTimer += frameDeltaSec;
                    if (s_ReportTimer >= REPORT_INTERVAL_SECONDS)
                    {
                        s_ReportTimer -= REPORT_INTERVAL_SECONDS;
                        shouldReport = true;
                    }
                }
                s_LastFrameTicks = now;
            }

            if (shouldReport)
            {
                Report();
            }
        }

        /// <summary>
        /// Call once per simulation tick. Tracks sim throughput separately from render FPS.
        /// Called from PerformanceProfilerSystem in SimulationSystemGroup.
        /// </summary>
        public static void OnSimTick()
        {
            // Always active — drives PERF report sim tick count
            lock (s_Lock)
            {
                s_SimTickCount++;
            }
        }

        // ═══════ Recording ═══════

        /// <summary>
        /// Record timing from external source (e.g., Harmony patches).
        /// Accepts raw Stopwatch ticks.
        /// </summary>
        public static void RecordExternal(string name, long ticks)
        {
            Record(name, ticks, isTopLevel: true);
        }

        /// <summary>
        /// Record timing from auto-profiled vanilla systems.
        /// Uses isTopLevel: false to avoid inflating CivicSurvival total in SimulationProfilerPatch.
        /// </summary>
        public static void RecordExternalVanilla(string name, long ticks)
        {
            Record(name, ticks, isTopLevel: false);
        }

        /// <summary>
        /// Record entity count at job schedule time. Displayed alongside timing in PERF.log.
        /// </summary>
        public static void RecordJobSchedule(string name, int entityCount)
        {
            // Always active — entity counts enrich PERF report
            lock (s_Lock)
            {
                s_JobEntityCounts[name] = entityCount;
            }
        }

        /// <summary>
        /// Record a native allocation for per-system tracking. Debug-only (gate at call site).
        /// </summary>
        public static void RecordAllocation(string systemName, int bytes)
        {
            if (!Enabled) return;
            lock (s_Lock)
            {
                if (!s_AllocationData.TryGetValue(systemName, out var data))
                {
                    data = new AllocationData();
                    s_AllocationData[systemName] = data;
                }
                data.TotalBytes += bytes;
                data.CallCount++;
            }
        }

#pragma warning disable S3398 // Record uses static s_Data - can't move inside struct
        private static void Record(string name, long ticks, bool isTopLevel, bool gcHit = false)
        {
#pragma warning restore S3398
            if (!Enabled) return;

            bool isSlow = false;
            double ms = 0;

            lock (s_Lock)
            {
                if (!s_Data.TryGetValue(name, out var data))
                {
                    data = new ProfileData();
                    s_Data[name] = data;
                }

                data.TotalTicks += ticks;
                data.CallCount++;

                // Only count top-level measurements to avoid double-counting nested markers
                // (e.g., TMS.OnUpdate 5ms contains CollectInputs 4ms — count 5ms, not 9ms)
                if (isTopLevel)
                    s_TotalMeasuredTicks += ticks;

                if (ticks > data.MaxTicks)
                    data.MaxTicks = ticks;

                // Track GC-affected vs clean separately
                if (gcHit)
                {
                    data.GcHitCount++;
                }
                else
                {
                    data.TotalTicksNoGC += ticks;
                    if (ticks > data.MaxTicksNoGC)
                        data.MaxTicksNoGC = ticks;
                }

                ms = ticks * MS_PER_SECOND / Stopwatch.Frequency;
                if (ms > SLOW_THRESHOLD_MS)
                {
                    data.SlowCount++;
                    isSlow = true;
                }
            }

            // Log slow operations if enabled
            if (isSlow && LogSlowImmediately)
            {
                PerfLogWriter.Write($"[SLOW] {name}: {ms:F1}ms{(gcHit ? " [GC]" : "")}");
            }
        }

        // ═══════ Report Orchestration ═══════

        private static void Report()
        {
            lock (s_Lock)
            {
                // Snapshot data and clear inside lock (may be empty when all domains off).
                // Reuse static buffers — Report() is main-thread only.
                s_SnapshotBuffer.Clear();
                foreach (var kvp in s_Data) s_SnapshotBuffer.Add(kvp);
                s_Data.Clear();

                s_JobEntityBuffer.Clear();
                foreach (var kvp in s_JobEntityCounts) s_JobEntityBuffer[kvp.Key] = kvp.Value;
                s_JobEntityCounts.Clear();

                s_AllocationBuffer.Clear();
                foreach (var kvp in s_AllocationData) s_AllocationBuffer.Add(kvp);
                s_AllocationData.Clear();

                // Keep the simulation-delta baseline report-scoped when the debug
                // SimulationProfilerPatch is not active, so enabling it mid-session
                // cannot inherit hours of production measurements.
                s_TotalMeasuredTicks = 0;
            }

            var snapshot = s_SnapshotBuffer;
            var jobEntitySnapshot = s_JobEntityBuffer;
            var allocationSnapshot = s_AllocationBuffer;

            PerfLogWriter.Write($"");
            int reportNumber;
            lock (s_Lock)
            {
                reportNumber = ++s_ReportCount;
            }
            PerfLogWriter.Write($"══════ Report #{reportNumber} ({DateTime.Now:HH:mm:ss}) ══════");

            // FPS stats — always reported (even when no systems active)
            long fpsSum, fpsMax;
            int fpsSamples, simTicks;
            bool stateChanged;
            string stateLabel;
            lock (s_Lock)
            {
                fpsSum = s_FrameTimeSum;
                fpsMax = s_FrameTimeMax;
                fpsSamples = s_FrameTimeSamples;
                simTicks = s_SimTickCount;
                stateChanged = s_StateChanged;
                s_StateChanged = false;
                stateLabel = s_CurrentStateLabel;

                // Reset counters
                s_FrameTimeSum = 0;
                s_FrameTimeMax = 0;
                s_FrameTimeSamples = 0;
                s_SimTickCount = 0;
            }

            if (fpsSamples > 0)
            {
                double avgFrameMs = fpsSum * MS_PER_SECOND / Stopwatch.Frequency / fpsSamples;
                double maxFrameMs = fpsMax * MS_PER_SECOND / Stopwatch.Frequency;
                double avgFps = avgFrameMs > 0 ? MS_PER_SECOND / avgFrameMs : 0;
                double minFps = maxFrameMs > 0 ? MS_PER_SECOND / maxFrameMs : 0;  // max frame time = min fps
                double simPerSec = fpsSum > 0 ? simTicks * (double)Stopwatch.Frequency / fpsSum : 0;

                PerfLogWriter.Write($"Render: {avgFps:F1} FPS ({minFps:F1} min) | Frame: {avgFrameMs:F1}ms avg, {maxFrameMs:F1}ms max | Sim: {simPerSec:F0} ticks/s");

                // Measurement mode so the parser can separate honest from distorted
                // reports: prof=on adds per-system Stopwatch overhead; log=Debug adds
                // hot-path logging that masks worker-bound sync. Clean baseline =
                // prof=off log=Info (only OnFrame aggregate). Honest sync = prof=on log=Info.
                PerfLogWriter.Write($"MODE: prof={(Enabled ? "on" : "off")} log={(s_DebugEnabled ? "Debug" : "Info")}");

                // Per-report domain state so each block is self-describing — no
                // cross-file correlation with TOGGLE timestamps needed. Empty (line
                // skipped) until the first toggle of the session.
                if (stateLabel.Length > 0)
                    PerfLogWriter.Write($"STATE: {stateLabel}");

                // Engine counters (Threads / Render counts / GPU memory / GC.Collect) live in VanillaProfiler mod.

                // DELTA after domain toggle + state accumulation (under lock for thread safety)
                string deltaLine = null!;
                List<string> summaryLines = null!;
                lock (s_Lock)
                {
                    if (stateChanged && s_HasPreviousAverages && s_PrevAvgFps > 0)
                    {
                        double dFps = avgFps - s_PrevAvgFps;
                        double pctFps = dFps / s_PrevAvgFps * 100;
                        double dFrame = avgFrameMs - s_PrevAvgFrameMs;
                        double dSim = simPerSec - s_PrevSimTicksPerSec;
                        deltaLine = $"DELTA (since toggle): {dFps:+0.0;-0.0} FPS ({pctFps:+0;-0}%), {dFrame:+0.0;-0.0}ms frame, {dSim:+0;-0} sim/s";
                    }

                    s_PrevAvgFps = avgFps;
                    s_PrevAvgFrameMs = avgFrameMs;
                    s_PrevSimTicksPerSec = simPerSec;
                    s_HasPreviousAverages = true;

                    if (stateLabel.Length > 0)
                    {
                        if (!s_StateStats.TryGetValue(stateLabel, out var acc))
                        {
                            if (s_StateStats.Count >= MAX_STATE_STATS)
                            {
                                s_StateStats.Clear();
                            }
                            acc = new StateAccumulator();
                            s_StateStats[stateLabel] = acc;
                        }
                        acc.FpsSum += avgFps;
                        acc.FrameMsSum += avgFrameMs;
                        acc.SimTicksSum += simPerSec;
                        acc.SampleCount++;
                    }

                    if (reportNumber % STATE_SUMMARY_INTERVAL == 0 && s_StateStats.Count > 0)
                    {
                        summaryLines = new List<string>();
                        foreach (var kvp in s_StateStats)
                        {
                            var a = kvp.Value;
                            if (a.SampleCount == 0) continue;
                            double aFps = a.FpsSum / a.SampleCount;
                            double aFrame = a.FrameMsSum / a.SampleCount;
                            double aSim = a.SimTicksSum / a.SampleCount;
                            summaryLines.Add($"  {kvp.Key,-STATE_LABEL_COLUMN_WIDTH} {aFps,5:F1} FPS, {aFrame,5:F1}ms frame, {aSim,3:F0} sim/s  ({a.SampleCount} samples)");
                        }
                    }
                }

                if (deltaLine != null)
                    PerfLogWriter.Write(deltaLine);

                if (summaryLines != null)
                {
                    PerfLogWriter.Write($"");
                    PerfLogWriter.Write($"STATE COMPARISON (rolling {STATE_SUMMARY_INTERVAL * REPORT_INTERVAL_SECONDS:F0}s, unlisted domains = ON)");
                    PerfLogWriter.Write(new string('─', 60));
                    foreach (var line in summaryLines) PerfLogWriter.Write(line);
                }

                // ═══════ A/B Test sampling ═══════
                PerfABTest.ProcessSample(avgFps, avgFrameMs, simPerSec);

                PerfLogWriter.Write($"");
            }

            // System timings (only when data exists)
            if (snapshot.Count > 0)
            {
                // Sort by total time descending
                snapshot.Sort((a, b) => b.Value.TotalTicks.CompareTo(a.Value.TotalTicks));

                PerfLogWriter.Write($"SYSTEMS — main-thread cost (sync points, structural changes, ECB playback, sync foreach)");
                PerfLogWriter.Write($"{"SYSTEM",-40} {"CALLS",6} {"TOTAL",10} {"CLEAN",10} {"AVG",8} {"MAX",8} {"SLOW",6}");
                PerfLogWriter.Write(new string('─', 90));

                foreach (var kvp in snapshot)
                {
                    var d = kvp.Value;
                    if (d.CallCount == 0) continue;

                    string entityInfo = "";
                    if (jobEntitySnapshot.TryGetValue(kvp.Key, out int entCount))
                        entityInfo = $" [{entCount} ent]";

                    string slowMark = d.SlowCount > 0 ? $"x{d.SlowCount}" : "";
                    string gcMark = d.GcHitCount > 0 ? $" GC:{d.GcHitCount}" : "";
                    string cleanCol = d.GcHitCount > 0
                        ? d.CleanTotalMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture).PadLeft(CLEAN_COLUMN_WIDTH - 2) + "ms"
                        : new string(' ', CLEAN_COLUMN_WIDTH);
                    PerfLogWriter.Write($"{kvp.Key,-40} {d.CallCount,6} {d.TotalMs,9:F1}ms {cleanCol} {d.AvgMs,7:F2}ms {d.MaxMs,7:F1}ms {slowMark,6}{gcMark}{entityInfo}");
                }

                // GC pause summary: show clean max for affected systems
                PerfReportSections.ReportGcPauses(snapshot);

                // ECB Command Counts (per-system, mod-tracked only)
                PerfReportSections.ReportEcbCounts();

                // Native Allocation Tracking
                PerfReportSections.ReportAllocations(allocationSnapshot);
            }

            // Sync point cost (Full:X vs X delta — shows ECS job completion overhead)
            PerfReportSections.ReportSyncPoints(snapshot);

            // UI Binding profiling (JSON serialization + Coherent push cost)
            PerfReportSections.ReportUIBindings();

            // UI React render profiling (received from JS via trigger)
            PerfReportSections.ReportUIReact();

            // Memory stats (every report — even when no systems active)
            PerfReportSections.ReportMemory();

            // Entity count snapshot — correlate with FPS degradation
            PerfReportSections.ReportEntityCounts();
        }

        // ═══════ Toggle / State Tracking ═══════

        private static void ParseToggle(string message)
        {
            // Format: "TOGGLE {domain} = {ON|OFF}"
            int eqIdx = message.IndexOf(" = ", StringComparison.Ordinal);
            if (eqIdx < 0) return;

            int domainLength = eqIdx - TOGGLE_PREFIX.Length;
            if (domainLength <= 0 || eqIdx + 3 > message.Length)
                return;

            string domain = message.Substring(TOGGLE_PREFIX.Length, domainLength);
            // Value after " = " may carry a suffix (e.g. "ON (6 systems) @ 14h"), so
            // match the prefix rather than the whole token.
            bool isOn = message.Substring(eqIdx + 3).StartsWith("ON", StringComparison.Ordinal);

            lock (s_Lock)
            {
                s_DomainStates[domain] = isOn;
                s_CurrentStateLabel = BuildStateLabel();
                s_StateChanged = true;
            }
        }

        private static string BuildStateLabel()
        {
            if (s_DomainStates.Count == 0) return "";

            // Show only domains that are OFF (default = ON) for compact labels
            var offDomains = new List<string>();
            foreach (var kvp in s_DomainStates)
            {
                if (!kvp.Value)
                    offDomains.Add(kvp.Key);
            }

            if (offDomains.Count == 0) return "all=ON";
            offDomains.Sort(StringComparer.Ordinal);
            return string.Join(",", offDomains) + "=OFF";
        }

        // ═══════ MeasureScope ═══════

        /// <summary>
        /// Scope struct for measuring. Public to allow zero-alloc return from Measure().
        /// </summary>
        public readonly struct MeasureScope : IDisposable
        {
            private readonly string m_Name;
            private readonly long m_StartTicks;
            private readonly bool m_Started;
            private readonly bool m_IsTopLevel;
            private readonly int m_Gen2Count;

            public MeasureScope(string name)
            {
                m_Name = name;
                if (Enabled)
                {
                    m_IsTopLevel = s_NestingDepth == 0;
#pragma warning disable S3010 // ThreadStatic depth counter — intentional static write from struct
                    s_NestingDepth++;
#pragma warning restore S3010
                    m_Gen2Count = GC.CollectionCount(2);
                    m_StartTicks = Stopwatch.GetTimestamp();
                    m_Started = true;
                }
                else
                {
                    m_IsTopLevel = false;
                    m_StartTicks = 0;
                    m_Gen2Count = 0;
                    m_Started = false;
                }
            }

            public void Dispose()
            {
                // Guard on explicit start state — if measurement started, MUST decrement depth
                // even if Enabled was toggled off mid-measurement (prevents nesting corruption)
                if (!m_Started) return;
#pragma warning disable S3010, S2696 // ThreadStatic depth counter — intentional static write from struct
                s_NestingDepth--;
#pragma warning restore S3010, S2696
                if (!Enabled) return; // Skip recording but depth is fixed
                long elapsed = Stopwatch.GetTimestamp() - m_StartTicks;
                bool gcHit = GC.CollectionCount(2) > m_Gen2Count;
                Record(m_Name, elapsed, m_IsTopLevel, gcHit);
            }
        }
    }
}
