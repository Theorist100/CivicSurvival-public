using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// A/B test state machine for automated domain performance comparison.
    /// Alternates domain ON/OFF, collects FPS/frame/sim stats, outputs comparison.
    /// </summary>
    internal static class PerfABTest
    {
        private static readonly object s_Lock = new();

        // Debug-only perf harness. All mutable static state below is:
        //   CIVIC059 (non-volatile cross-thread bool): every read/write happens under s_Lock, so volatile is unnecessary.
        //   CIVIC080 (static event-handler field): s_ToggleAction holds a delegate, not a multicast event; lifecycle is bounded by the harness session and cleared in ResetWithoutCallbacks().
        //   CIVIC114 (lock asymmetry): every field listed is read/written under s_Lock at all call sites in this file.
        //   CIVIC148 (static collection without clear): all lists/queues are cleared in ResetWithoutCallbacks() before the next session.
        //   CIVIC166 (static field in service without reset): ResetWithoutCallbacks() restores defaults so state does not survive a world reload.
#pragma warning disable CIVIC080, CIVIC059, CIVIC114, CIVIC148, CIVIC166
        private static Action<bool> s_ToggleAction = null!;
        private static string s_DomainKey = null!;
        private static int s_CyclesRemaining;
        private static int s_ReportsPerPhase;
        private static int s_ReportsInPhase;
        private static bool s_CurrentlyOn;
        private static bool s_SkipNext;
        private static List<double> s_OnFps = null!, s_OffFps = null!;
        private static List<double> s_OnFrameMs = null!, s_OffFrameMs = null!;
        private static List<double> s_OnSimTicks = null!, s_OffSimTicks = null!;
        private static int s_TotalCycles;
        private static int s_RunId;
        private const float DEFAULT_REPORT_INTERVAL_SEC = 5.0f;
        private const int RESULT_KEY_WIDTH = 37;
        private static float s_ReportIntervalSec = DEFAULT_REPORT_INTERVAL_SEC;
        // Sequential A/B queue: run multiple toggles one after another
        private static List<(string key, Action<bool> toggle)> s_Queue = null!;
        private static int s_QueueIndex;
        private static int s_QueueCycles;
        private static int s_QueueReportsPerPhase;
#pragma warning restore CIVIC080, CIVIC059, CIVIC114, CIVIC148, CIVIC166

        public static bool Running
        {
            get { lock (s_Lock) return s_ToggleAction != null; }
        }

        internal static void ResetWithoutCallbacks()
        {
            lock (s_Lock)
            {
                s_ToggleAction = null!;
                s_DomainKey = string.Empty;
                s_CyclesRemaining = 0;
                s_ReportsPerPhase = 1;
                s_ReportsInPhase = 0;
                s_CurrentlyOn = true;
                s_SkipNext = false;
                s_OnFps = null!;
                s_OffFps = null!;
                s_OnFrameMs = null!;
                s_OffFrameMs = null!;
                s_OnSimTicks = null!;
                s_OffSimTicks = null!;
                s_TotalCycles = 0;
                s_ReportIntervalSec = DEFAULT_REPORT_INTERVAL_SEC;
                s_Queue = null!;
                s_QueueIndex = 0;
                s_QueueCycles = 0;
                s_QueueReportsPerPhase = 1;
                s_RunId++;
            }
        }

        /// <summary>
        /// Returns progress text for UI, e.g. "ON 4/6 cycle 2/3 ~1:25"
        /// </summary>
        public static string GetProgressText()
        {
            string domainKey;
            bool currentlyOn;
            int totalCycles;
            int cyclesRemaining;
            int reportsPerPhase;
            int reportsInPhase;
            float reportIntervalSec;
            int queueIndex;
            int queueCount;

            lock (s_Lock)
            {
                if (s_ToggleAction == null) return "";

                domainKey = s_DomainKey;
                currentlyOn = s_CurrentlyOn;
                totalCycles = s_TotalCycles;
                cyclesRemaining = s_CyclesRemaining;
                reportsPerPhase = s_ReportsPerPhase;
                reportsInPhase = s_ReportsInPhase;
                reportIntervalSec = s_ReportIntervalSec;
                queueIndex = s_QueueIndex;
                queueCount = s_Queue != null ? s_Queue.Count : 0;
            }

            string phase = currentlyOn ? "ON" : "OFF";
            int doneCycles = totalCycles - cyclesRemaining;
            int donePhases = doneCycles * 2 + (currentlyOn ? 0 : 1);
            int totalPhases = Math.Max(totalCycles * 2, 1);
            int samplesInPhase = Math.Max(reportsPerPhase, 1);

            // Time estimate: each phase = (reportsPerPhase * interval) + interval warmup
            float phaseSeconds = samplesInPhase * reportIntervalSec + reportIntervalSec;
            float remainingPhases = totalPhases - donePhases;
            float remainingInCurrentPhase = (samplesInPhase - reportsInPhase) * reportIntervalSec;
            float totalRemaining = (remainingPhases - 1) * phaseSeconds + remainingInCurrentPhase;
            if (totalRemaining < 0) totalRemaining = 0;

            const int secondsPerMinute = 60;
#pragma warning disable CIVIC177 // Countdown timer: truncation toward zero is intentional
            int mins = (int)(totalRemaining / secondsPerMinute);
            int secs = (int)(totalRemaining % secondsPerMinute);
#pragma warning restore CIVIC177

            if (queueCount > 0)
                return $"{domainKey} {phase} {reportsInPhase + 1}/{samplesInPhase} [{queueIndex + 1}/{queueCount}] ~{mins}:{secs:D2}";
            return $"{phase} {reportsInPhase + 1}/{samplesInPhase} | {donePhases + 1}/{totalPhases} | ~{mins}:{secs:D2}";
        }

        /// <summary>
        /// Start automated A/B test: alternates domain ON/OFF every (reportsPerPhase * interval),
        /// collects FPS/frame/sim stats per phase, outputs comparison with stddev after all cycles.
        /// </summary>
        public static void Start(string domainKey, int cycles, int reportsPerPhase, Action<bool> toggleAction, float reportIntervalSec)
        {
            int safeCycles = Math.Max(cycles, 1);
            int safeReportsPerPhase = Math.Max(reportsPerPhase, 1);
            lock (s_Lock)
            {
                s_Queue = null!;
                s_QueueIndex = 0;
                s_QueueCycles = 0;
                s_QueueReportsPerPhase = 1;
                StartLocked(domainKey, safeCycles, safeReportsPerPhase, toggleAction, reportIntervalSec);
            }
            PerfLogWriter.WriteMarker($"A/B START: {domainKey}, {safeCycles} cycles × {safeReportsPerPhase * reportIntervalSec:F0}s per phase");
        }

        /// <summary>
        /// Cancel running A/B test. Restores domain to ON state.
        /// </summary>
        public static void Stop()
        {
            Action<bool> toggle;
            ResultSnapshot partial;
            lock (s_Lock)
            {
                toggle = s_ToggleAction;
                partial = CaptureResultLocked("A/B PARTIAL");
                s_ToggleAction = null!;
                s_Queue = null!;
                s_RunId++;
            }

            if (toggle != null)
            {
                toggle(true); // restore ON
                PerfLogWriter.WriteMarker("A/B CANCELLED");
                if (partial.HasAnyPairedSamples)
                    WriteResult(partial);
            }
        }

        /// <summary>
        /// Start sequential A/B tests: tests each toggle one after another automatically.
        /// </summary>
        public static void StartSequence(string[] keys, Action<bool>[] toggles, int cycles, int reportsPerPhase, float reportIntervalSec)
        {
            if (keys == null || toggles == null || keys.Length == 0 || toggles.Length == 0)
                return;

            int count = Math.Min(keys.Length, toggles.Length);
            int safeCycles = Math.Max(cycles, 1);
            int safeReportsPerPhase = Math.Max(reportsPerPhase, 1);
            lock (s_Lock)
            {
                s_Queue = new List<(string, Action<bool>)>(count);
                for (int i = 0; i < count; i++)
                    s_Queue.Add((keys[i], toggles[i]));
                s_QueueIndex = 0;
                s_QueueCycles = safeCycles;
                s_QueueReportsPerPhase = safeReportsPerPhase;
                StartLocked(keys[0], safeCycles, safeReportsPerPhase, toggles[0], reportIntervalSec);
            }
            PerfLogWriter.WriteMarker($"A/B SEQUENCE START: {count} tests × {safeCycles} cycles");
            PerfLogWriter.WriteMarker($"A/B START: {keys[0]}, {safeCycles} cycles × {safeReportsPerPhase * reportIntervalSec:F0}s per phase");
        }

        /// <summary>
        /// Process one report sample during A/B test. Called from Report().
        /// </summary>
        public static void ProcessSample(double avgFps, double avgFrameMs, double simPerSec)
        {
            Action<bool> toggleToInvoke = null!;
            bool toggleValue = true;
            int toggleRunId = 0;
            string logLine = null!;
            string markerLine = null!;
            string nextMarkerLine = null!;
            ResultSnapshot result = default;

            lock (s_Lock)
            {
                var toggle = s_ToggleAction;
                if (toggle == null) return;

                if (s_SkipNext)
                {
                    s_SkipNext = false;
                    logLine = $"[A/B] skip warmup ({s_DomainKey} {(s_CurrentlyOn ? "ON" : "OFF")})";
                }
                else
                {
                    List<double> fps = s_CurrentlyOn ? s_OnFps : s_OffFps;
                    List<double> frameMs = s_CurrentlyOn ? s_OnFrameMs : s_OffFrameMs;
                    List<double> simTicks = s_CurrentlyOn ? s_OnSimTicks : s_OffSimTicks;

                    fps.Add(avgFps);
                    frameMs.Add(avgFrameMs);
                    simTicks.Add(simPerSec);

                    s_ReportsInPhase++;
                    bool sampleOn = s_CurrentlyOn;
                    logLine = $"[A/B] sample {s_ReportsInPhase}/{s_ReportsPerPhase} ({(sampleOn ? "ON" : "OFF")}, cycle {s_CyclesRemaining})";

                    if (s_ReportsInPhase >= s_ReportsPerPhase)
                    {
                        s_ReportsInPhase = 0;

                        if (!s_CurrentlyOn)
                        {
                            s_CyclesRemaining--;
                            if (s_CyclesRemaining <= 0)
                            {
                                string completedDomain = s_DomainKey;
                                result = CaptureResultLocked("A/B RESULT");
                                toggleToInvoke = toggle;
                                toggleValue = true;
                                toggleRunId = s_RunId;

                                bool hasNext = s_Queue != null && s_QueueIndex + 1 < s_Queue.Count;
                                if (hasNext)
                                {
                                    s_QueueIndex++;
                                    var next = s_Queue![s_QueueIndex];
                                    int queueIndex = s_QueueIndex;
                                    int queueCount = s_Queue!.Count;
                                    nextMarkerLine = $"A/B NEXT: {next.key} ({queueIndex + 1}/{queueCount})";
                                    StartLocked(next.key, s_QueueCycles, s_QueueReportsPerPhase, next.toggle, s_ReportIntervalSec);
                                    s_SkipNext = true;
                                }
                                else
                                {
                                    bool wasSequence = s_Queue != null;
                                    s_ToggleAction = null!;
                                    s_Queue = null!;
                                    s_QueueIndex = 0;
                                    s_RunId++;
                                    markerLine = wasSequence ? "A/B SEQUENCE COMPLETE" : $"A/B COMPLETE: {completedDomain}";
                                }
                            }
                        }

                        if (toggleToInvoke == null && s_ToggleAction != null)
                        {
                            s_CurrentlyOn = !s_CurrentlyOn;
                            s_SkipNext = true;
                            toggleToInvoke = toggle;
                            toggleValue = s_CurrentlyOn;
                            toggleRunId = s_RunId;
                            markerLine = $"A/B PHASE: {s_DomainKey} = {(s_CurrentlyOn ? "ON" : "OFF")} (cycles left: {s_CyclesRemaining})";
                        }
                    }
                }
            }

            if (logLine != null) PerfLogWriter.Write(logLine);
            if (result.HasAnySamples) WriteResult(result);
            if (toggleToInvoke != null) InvokeToggleWithStopReconcile(toggleToInvoke, toggleRunId, toggleValue);
            if (nextMarkerLine != null) PerfLogWriter.WriteMarker(nextMarkerLine);
            if (markerLine != null) PerfLogWriter.WriteMarker(markerLine);
        }

        private static void StartLocked(string domainKey, int cycles, int reportsPerPhase, Action<bool> toggleAction, float reportIntervalSec)
        {
            lock (s_Lock)
            {
                s_DomainKey = domainKey ?? string.Empty;
                s_TotalCycles = cycles;
                s_CyclesRemaining = cycles;
                s_ReportsPerPhase = reportsPerPhase;
                s_ReportsInPhase = 0;
                s_CurrentlyOn = true;
                s_SkipNext = false;
                s_ToggleAction = toggleAction;
                s_ReportIntervalSec = reportIntervalSec > 0f ? reportIntervalSec : DEFAULT_REPORT_INTERVAL_SEC;
                s_OnFps = new List<double>();
                s_OffFps = new List<double>();
                s_OnFrameMs = new List<double>();
                s_OffFrameMs = new List<double>();
                s_OnSimTicks = new List<double>();
                s_OffSimTicks = new List<double>();
                s_RunId++;
            }
        }

        private static void InvokeToggleWithStopReconcile(Action<bool> toggle, int runId, bool value)
        {
            toggle(value);
            if (value) return;

            bool stopped;
            lock (s_Lock)
            {
                stopped = s_ToggleAction != toggle || s_RunId != runId;
            }

            if (stopped)
                toggle(true);
        }

        private static ResultSnapshot CaptureResultLocked(string title)
        {
            if (s_OnFps == null || s_OffFps == null)
                return default;

            return new ResultSnapshot(
                title,
                s_DomainKey ?? string.Empty,
                Mean(s_OnFps),
                Mean(s_OffFps),
                StdDev(s_OnFps),
                StdDev(s_OffFps),
                Mean(s_OnFrameMs),
                Mean(s_OffFrameMs),
                StdDev(s_OnFrameMs),
                StdDev(s_OffFrameMs),
                Mean(s_OnSimTicks),
                Mean(s_OffSimTicks),
                s_OnFps.Count,
                s_OffFps.Count);
        }

        private static void WriteResult(ResultSnapshot result)
        {
            string key = Truncate(result.DomainKey, RESULT_KEY_WIDTH);
            if (!result.HasEnoughSamples)
            {
                PerfLogWriter.Write("");
                PerfLogWriter.Write($"{result.Title}: {key}");
                PerfLogWriter.Write($"Insufficient samples (on={result.OnSamples}, off={result.OffSamples})");
                return;
            }

            double deltaFps = result.OffFpsAvg - result.OnFpsAvg;
            double deltaFrame = result.OffFrameAvg - result.OnFrameAvg;

            PerfLogWriter.Write("");
            PerfLogWriter.Write($"{result.Title}: {key}");
            PerfLogWriter.Write($"ON:  {result.OnFpsAvg,5:F1} ± {result.OnFpsStd,4:F1} FPS | {result.OnFrameAvg,5:F1} ± {result.OnFrameStd,4:F1} ms | {result.OnSimAvg,3:F0} sim/s  ({result.OnSamples} samples)");
            PerfLogWriter.Write($"OFF: {result.OffFpsAvg,5:F1} ± {result.OffFpsStd,4:F1} FPS | {result.OffFrameAvg,5:F1} ± {result.OffFrameStd,4:F1} ms | {result.OffSimAvg,3:F0} sim/s  ({result.OffSamples} samples)");
            PerfLogWriter.Write($"Delta: {deltaFps,+5:+0.0;-0.0} FPS | {deltaFrame,+5:+0.0;-0.0} ms");
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            if (maxLength <= 3)
                return value.Substring(0, maxLength);
            return value.Substring(0, maxLength - 3) + "...";
        }

        private static double Mean(List<double> values)
        {
            if (values.Count == 0) return 0;
            double sum = 0;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = Mean(values);
            double sumSq = 0;
            foreach (var v in values)
            {
                double d = v - mean;
                sumSq += d * d;
            }
            return Math.Sqrt(sumSq / Math.Max(values.Count - 1, 1));
        }

        private readonly struct ResultSnapshot : IEquatable<ResultSnapshot>
        {
            public readonly string Title;
            public readonly string DomainKey;
            public readonly double OnFpsAvg;
            public readonly double OffFpsAvg;
            public readonly double OnFpsStd;
            public readonly double OffFpsStd;
            public readonly double OnFrameAvg;
            public readonly double OffFrameAvg;
            public readonly double OnFrameStd;
            public readonly double OffFrameStd;
            public readonly double OnSimAvg;
            public readonly double OffSimAvg;
            public readonly int OnSamples;
            public readonly int OffSamples;

            public ResultSnapshot(
                string title,
                string domainKey,
                double onFpsAvg,
                double offFpsAvg,
                double onFpsStd,
                double offFpsStd,
                double onFrameAvg,
                double offFrameAvg,
                double onFrameStd,
                double offFrameStd,
                double onSimAvg,
                double offSimAvg,
                int onSamples,
                int offSamples)
            {
                Title = title;
                DomainKey = domainKey;
                OnFpsAvg = onFpsAvg;
                OffFpsAvg = offFpsAvg;
                OnFpsStd = onFpsStd;
                OffFpsStd = offFpsStd;
                OnFrameAvg = onFrameAvg;
                OffFrameAvg = offFrameAvg;
                OnFrameStd = onFrameStd;
                OffFrameStd = offFrameStd;
                OnSimAvg = onSimAvg;
                OffSimAvg = offSimAvg;
                OnSamples = onSamples;
                OffSamples = offSamples;
            }

            public bool HasAnySamples => OnSamples > 0 || OffSamples > 0;
            public bool HasAnyPairedSamples => OnSamples > 0 && OffSamples > 0;
            public bool HasEnoughSamples => OnSamples >= 2 && OffSamples >= 2;

            public bool Equals(ResultSnapshot other)
                => string.Equals(Title, other.Title, StringComparison.Ordinal)
                    && string.Equals(DomainKey, other.DomainKey, StringComparison.Ordinal)
                    && OnFpsAvg.Equals(other.OnFpsAvg)
                    && OffFpsAvg.Equals(other.OffFpsAvg)
                    && OnFpsStd.Equals(other.OnFpsStd)
                    && OffFpsStd.Equals(other.OffFpsStd)
                    && OnFrameAvg.Equals(other.OnFrameAvg)
                    && OffFrameAvg.Equals(other.OffFrameAvg)
                    && OnFrameStd.Equals(other.OnFrameStd)
                    && OffFrameStd.Equals(other.OffFrameStd)
                    && OnSimAvg.Equals(other.OnSimAvg)
                    && OffSimAvg.Equals(other.OffSimAvg)
                    && OnSamples == other.OnSamples
                    && OffSamples == other.OffSamples;

            public override bool Equals(object? obj)
                => obj is ResultSnapshot other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Title, StringComparer.Ordinal);
                hash.Add(DomainKey, StringComparer.Ordinal);
                hash.Add(OnFpsAvg);
                hash.Add(OffFpsAvg);
                hash.Add(OnFrameAvg);
                hash.Add(OffFrameAvg);
                hash.Add(OnSamples);
                hash.Add(OffSamples);
                return hash.ToHashCode();
            }

            public static bool operator ==(ResultSnapshot left, ResultSnapshot right)
                => left.Equals(right);

            public static bool operator !=(ResultSnapshot left, ResultSnapshot right)
                => !left.Equals(right);
        }
    }
}
