using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Game.Simulation;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;


namespace CivicSurvival.Patches
{
    /// <summary>
    /// DEBUG: Profiles entire SimulationSystemGroup to find "missing" frame time.
    /// Compares total simulation time vs our profiled systems.
    ///
    /// Output to PERF.log:
    /// === Frame Analysis ===
    /// SimulationGroup Total: 447ms
    /// CivicSurvival Systems: 50ms
    /// Vanilla/Unknown: 397ms (89%)
    /// </summary>
#pragma warning disable CIVIC092 // OnUpdate is protected — nameof inaccessible
    [HarmonyPatch(typeof(SimulationSystem), "OnUpdate")]
#pragma warning restore CIVIC092
    public static class SimulationProfilerPatch
    {
        private const string PatchName = nameof(SimulationProfilerPatch);
        private static readonly LogContext Log = new("SimulationProfiler");
        private const double MS_PER_SECOND = 1000.0;

        // FIX #115: Verify target method exists before applying patch.
        // If game renames "OnUpdate", patch silently skips instead of failing.
        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (!Log.IsDebugEnabled) return false;
            var method = AccessTools.Method(typeof(SimulationSystem), "OnUpdate");
            if (method == null)
            {
                Log.Warn("SimulationSystem.OnUpdate not found — profiler patch will not apply");
                return false;
            }
            return true;
        }

        // Accumulate over report interval (high-precision ticks, NOT truncated ms)
        private static long s_TotalSimulationTicks;
        private static int s_FrameCount;
        private static long s_MaxFrameTicks;

        // FIX: Track only simulation-phase civic time (not rendering/UI phases)
        // Old bug: GetTotalMeasuredMs() included CameraTrackingSystem, UI systems etc.
        // that run OUTSIDE SimulationSystem.OnUpdate → "CivicSurvival Systems" > "Total"
        private static double s_AccumCivicSimMs;

#pragma warning disable CA1815 // Equals/GetHashCode unnecessary — used only as transient per-call profiler state.
        public struct ProfileState
        {
            public long StartTicks;
            public float CivicSnapshot;
        }
#pragma warning restore CA1815

        // Report every N frames
        private const int REPORT_INTERVAL = 300;

        internal static void ResetCounters()
        {
            Interlocked.Exchange(ref s_TotalSimulationTicks, 0);
            Interlocked.Exchange(ref s_FrameCount, 0);
            Interlocked.Exchange(ref s_MaxFrameTicks, 0);
            s_AccumCivicSimMs = 0.0;
        }

        public static void Cleanup()
        {
            ResetCounters();
        }

        public static void VerifyAndReport()
        {
            // Debug-off: Prepare() returned false so no patch was applied — that is the intended
            // production state, not a missing-patch failure. Report success without inspecting Harmony.
            if (!Log.IsDebugEnabled)
            {
                PatchStatusTracker.ReportSuccess(PatchName);
                return;
            }

            PatchStatusTracker.VerifyPatchInfo(
                PatchName,
                AccessTools.Method(typeof(SimulationSystem), "OnUpdate"),
                "SimulationSystem.OnUpdate",
                typeof(SimulationProfilerPatch),
                expectPrefix: true,
                expectPostfix: true);
        }

        [HarmonyPrefix]
        public static void Prefix(out ProfileState __state)
        {
            __state = new ProfileState
            {
                StartTicks = Stopwatch.GetTimestamp(),
                CivicSnapshot = PerformanceProfiler.GetTotalMeasuredMs()
            };
        }

        [HarmonyPostfix]
        public static void Postfix(ProfileState __state)
        {
            // Patch is applied unconditionally when Debug logging is on (see Prepare),
            // but vanilla SimulationSystem.OnUpdate also ticks in the main menu before
            // any city is loaded. Without this gate the 300-frame report fires from
            // menu with civicAvgMs=0 and spams "=== Frame Analysis" into the main log.
            // Mirror the PerformanceProfiler.IsInitialized contract: profiler dormant
            // → patch quietly drops the sample.
            if (!PerformanceProfiler.IsInitialized) return;

            try
            {
                // FIX: Use ElapsedTicks (not ElapsedMilliseconds which truncates to int)
                // Truncation caused civic > total → negative Vanilla/Unknown values
                long frameTicks = Stopwatch.GetTimestamp() - __state.StartTicks;

                // Delta = only our systems that ran during THIS SimulationSystem.OnUpdate tick
                float civicDelta = PerformanceProfiler.GetTotalMeasuredMs() - __state.CivicSnapshot;
                s_AccumCivicSimMs += civicDelta;

                Interlocked.Add(ref s_TotalSimulationTicks, frameTicks);
                Interlocked.Increment(ref s_FrameCount);
                // Interlocked compare-exchange loop for max
                long currentMax = Interlocked.Read(ref s_MaxFrameTicks);
                while (frameTicks > currentMax)
                {
                    long prev = Interlocked.CompareExchange(ref s_MaxFrameTicks, frameTicks, currentMax);
                    if (prev == currentMax) break;
                    currentMax = prev;
                }

                int frameCount = Interlocked.CompareExchange(ref s_FrameCount, 0, 0); // atomic read
                if (frameCount >= REPORT_INTERVAL)
                {
                    double ticksToMs = MS_PER_SECOND / Stopwatch.Frequency;
                    float avgMs = (float)(Interlocked.Read(ref s_TotalSimulationTicks) * ticksToMs / frameCount);
                    float civicAvgMs = (float)(s_AccumCivicSimMs / frameCount);

                    float unknownMs = avgMs - civicAvgMs;
                    float unknownPct = avgMs > 0 ? (unknownMs / avgMs * 100f) : 0f;

                    float maxFrameMs = (float)(Interlocked.Read(ref s_MaxFrameTicks) * ticksToMs);
                    Log.Info($"=== Frame Analysis ({frameCount} frames) ===");
                    Log.Info($"SimulationSystem Total: {avgMs:F1}ms avg, {maxFrameMs:F1}ms max");
                    Log.Info($"CivicSurvival Systems (sim only): {civicAvgMs:F1}ms avg");
                    Log.Info($"Vanilla/Unknown: {unknownMs:F1}ms avg ({unknownPct:F0}%)");

                    // Reset (atomic)
                    Interlocked.Exchange(ref s_TotalSimulationTicks, 0);
                    Interlocked.Exchange(ref s_FrameCount, 0);
                    Interlocked.Exchange(ref s_MaxFrameTicks, 0);
                    s_AccumCivicSimMs = 0;
                    PerformanceProfiler.ResetTotalMeasured();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Profiler postfix error: {ex}");
            }
        }
    }
}
