using System;
using System.Globalization;
using System.IO;
using System.Text;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Last low-cost gameplay/runtime snapshot for native-crash breadcrumbs.
    /// Updated by TelemetryPulse at the perf-sample cadence and persisted so an
    /// unmarked unclean shutdown can still report the last known state on next boot.
    /// </summary>
    public static class CrashContextProvider
    {
        private const int MaxContextFileBytes = 2048;
        private static readonly LogContext Log = new("CrashContextProvider");
        private static readonly object s_Lock = new();

        private static CrashContextSnapshot s_Current = CrashContextSnapshot.Empty;
        // In-memory initial phase. Deliberately NOT persisted at boot: a write here would clobber the
        // crashed session's context file before DetectPreviousCrash recovers it. The first on-disk phase
        // write is the first transition AFTER recovery (Loading/Menu), which runs post-ClearPersisted.
        private static LifecyclePhase s_Phase = LifecyclePhase.Boot;
        private static DateTime s_HeartbeatUtc = DateTime.MinValue;

        public static CrashContextSnapshot Current
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Current;
                }
            }
        }

        public static void Update(CrashContextSnapshot snapshot)
        {
            lock (s_Lock)
            {
                s_Current = snapshot;
#pragma warning disable CIVIC003 // Diagnostic wall-clock heartbeat: compared against the minidump UTC on recovery; game-time/TickCount are not comparable to the dump timestamp. Pulse cadence, not per-frame.
                s_HeartbeatUtc = DateTime.UtcNow;
#pragma warning restore CIVIC003
            }

            Persist(snapshot);
        }

        /// <summary>
        /// Record the current process lifecycle phase for crash classification (phase × fault-code on
        /// the dashboard — e.g. a <see cref="LifecyclePhase.Loading"/> ANR is a legitimate long sync op,
        /// not an in-game freeze). Idempotent: only a real phase change touches disk, so per-tick callers
        /// (e.g. the save→sim return on the telemetry pulse) are no-ops until the phase actually changes.
        /// Phase transitions are event-driven and rare, so the immediate write is not a hot path
        /// (Axiom 15 / PERF-LOCK).
        /// </summary>
        public static void SetPhase(LifecyclePhase phase)
        {
            CrashContextSnapshot snapshot;
            LifecyclePhase previous;
            lock (s_Lock)
            {
                // PERF-LOCK: gate the write on an actual phase change — per-tick callers must be free.
                if (s_Phase == phase)
                    return;
                previous = s_Phase;
                s_Phase = phase;
#pragma warning disable CIVIC003 // Diagnostic wall-clock heartbeat: compared against the minidump UTC on recovery; game-time/TickCount are not comparable to the dump timestamp. Phase-change cadence, not per-tick.
                s_HeartbeatUtc = DateTime.UtcNow;
#pragma warning restore CIVIC003
                snapshot = s_Current;
            }

            Persist(snapshot);
            // Transition only (gated above on an actual change), never per-tick — a low-volume lifecycle
            // line so the crash-heartbeat phase progression is visible in the log (Axiom 1: logs = truth).
            Log.Info($" Phase: {previous} -> {phase}");
        }

        /// <summary>
        /// Drop the current city's snapshot at a save-load / city-change boundary. CS2 reuses
        /// the process, so without this the static + persisted file keep the PREVIOUS city's
        /// state, and a crash early in the new city (before its first telemetry pulse) would
        /// report the old city's day/wave/population. Called from the GameLoaded handler.
        /// </summary>
        public static void Reset()
        {
            lock (s_Lock)
            {
                s_Current = CrashContextSnapshot.Empty;
            }

            ClearPersisted();
        }

        public static bool TryReadPersisted(out CrashContextSnapshot snapshot, out string modVersion, out string sessionId, out string recentMarkers, out string phase, out string heartbeatUtc)
        {
            snapshot = CrashContextSnapshot.Empty;
            modVersion = string.Empty;
            sessionId = string.Empty;
            recentMarkers = string.Empty;
            phase = string.Empty;
            heartbeatUtc = string.Empty;

            try
            {
                var info = new FileInfo(ContextPath);
                info.Refresh();
                if (!info.Exists)
                    return false;

                if (info.Length > MaxContextFileBytes)
                {
                    ClearPersisted();
                    Log.Warn($" Discarded oversized native crash context file: {info.Length} bytes");
                    return false;
                }

                using var reader = new StreamReader(info.FullName, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var json = reader.ReadToEnd();
                return TryParse(json, out snapshot, out modVersion, out sessionId, out recentMarkers, out phase, out heartbeatUtc);
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash context recovery failed: {ex}");
                return false;
            }
        }

        public static void ClearPersisted()
        {
            try
            {
                File.Delete(ContextPath);
            }
            catch (DirectoryNotFoundException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Native crash context directory already absent: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Native crash context already absent: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash context clear failed: {ex}");
            }
        }

        internal static bool TryParse(string json, out CrashContextSnapshot snapshot, out string modVersion, out string sessionId, out string recentMarkers, out string phase, out string heartbeatUtc)
        {
            snapshot = CrashContextSnapshot.Empty;
            modVersion = string.Empty;
            sessionId = string.Empty;
            recentMarkers = string.Empty;
            phase = string.Empty;
            heartbeatUtc = string.Empty;

            try
            {
                var values = JsonStream.ParseFlatStringDict(json);
                snapshot = new CrashContextSnapshot(
                    ReadInt(values, "gameDay"),
                    ReadString(values, "act"),
                    ReadInt(values, "activeThreats"),
                    ReadInt(values, "waveNumber"),
                    ReadInt(values, "population"),
                    ReadInt(values, "managedMemoryMb"),
                    ReadInt(values, "nativeMemoryMb"),
                    ReadUtc(values, "capturedUtc"));
                modVersion = ReadString(values, "modVersion");
                sessionId = ReadString(values, "sessionId");
                recentMarkers = ReadString(values, "recentMarkers");
                phase = ReadString(values, "phase");
                heartbeatUtc = ReadString(values, "heartbeatUtc");
                // A boot/menu/loading crash carries a phase but no gameplay snapshot — treat the context
                // as present when EITHER a snapshot or a phase was recovered, so a load-phase ANR is not
                // dropped as "empty".
                return !snapshot.IsEmpty || !string.IsNullOrEmpty(phase);
            }
            catch (FormatException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Ignored malformed native crash context JSON: {ex.Message}");
                return false;
            }
        }

        internal static string ToJson(in CrashContextSnapshot snapshot, LifecyclePhase phase, DateTime heartbeatUtc)
        {
            // Same crashing-run identity the marker file carries, so the unmarked-crash
            // fallback path (no marker file → recovery reads this context file) still reports
            // the build/session that crashed, not the relaunch session that recovers it.
            var identity = CrashBreadcrumbIdentity.Current;
            return JsonBuilder.Object()
                .Add("gameDay", snapshot.GameDay.ToString(CultureInfo.InvariantCulture))
                .Add("act", snapshot.Act)
                .Add("activeThreats", snapshot.ActiveThreats.ToString(CultureInfo.InvariantCulture))
                .Add("waveNumber", snapshot.WaveNumber.ToString(CultureInfo.InvariantCulture))
                .Add("population", snapshot.Population.ToString(CultureInfo.InvariantCulture))
                .Add("managedMemoryMb", snapshot.ManagedMemoryMb.ToString(CultureInfo.InvariantCulture))
                .Add("nativeMemoryMb", snapshot.NativeMemoryMb.ToString(CultureInfo.InvariantCulture))
                .Add("capturedUtc", snapshot.CapturedUtc.ToString("O", CultureInfo.InvariantCulture))
                .Add("modVersion", identity.ModVersion)
                .Add("sessionId", identity.SessionId)
                // Lifecycle phase + heartbeat for phase × fault-code crash classification on recovery.
                // Phase is a whitelisted enum (GDPR-safe); heartbeat is the last moment our code was
                // confirmed live (distinguishes a freeze-during-work from a long suspend/idle).
                .Add("phase", phase.ToString())
                .Add("heartbeatUtc", heartbeatUtc == DateTime.MinValue ? string.Empty : heartbeatUtc.ToString("O", CultureInfo.InvariantCulture))
                // Phase sequence mirrored from the breadcrumb ring so it survives an UNMARKED unclean
                // shutdown (no marker file). Live read at write-time, like CrashBreadcrumbIdentity above.
                .Add("recentMarkers", NativeCrashBreadcrumb.GetRecentMarkers())
                .Build();
        }

        private static void Persist(CrashContextSnapshot snapshot)
        {
            LifecyclePhase phase;
            DateTime heartbeatUtc;
            lock (s_Lock)
            {
                phase = s_Phase;
                heartbeatUtc = s_HeartbeatUtc;
            }

            // Persist even when the gameplay snapshot is empty: boot/menu/loading carry no gameplay
            // state, but their phase + heartbeat must still survive for crash classification.
            if (snapshot.IsEmpty && phase == LifecyclePhase.Unknown)
                return;

            try
            {
                Directory.CreateDirectory(ModPaths.ModDataDirectory);
                // Direct in-place write (1 syscall) instead of atomic temp+replace (3-4): runs on the
                // main thread at the perf-sample / phase-transition cadence into the OneDrive/AV ANR
                // zone. A torn context record is discarded by TryReadPersisted (malformed / empty).
                AtomicFileWriter.WriteAllTextDirect(ContextPath, ToJson(snapshot, phase, heartbeatUtc));
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash context write failed: {ex}");
            }
        }

        private static string ReadString(System.Collections.Generic.Dictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value) ? value : string.Empty;

        private static int ReadInt(System.Collections.Generic.Dictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

        private static DateTime ReadUtc(System.Collections.Generic.Dictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value)
            && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;

        private static string ContextPath
            => Path.Combine(ModPaths.ModDataDirectory, ModPaths.NativeCrashContextFile);
    }

    public readonly struct CrashContextSnapshot : IEquatable<CrashContextSnapshot>
    {
        private const int HashMultiplier = 397;

        public readonly int GameDay;
        public readonly string Act;
        public readonly int ActiveThreats;
        public readonly int WaveNumber;
        public readonly int Population;
        public readonly int ManagedMemoryMb;
        public readonly int NativeMemoryMb;
        public readonly DateTime CapturedUtc;

        public CrashContextSnapshot(
            int gameDay,
            string act,
            int activeThreats,
            int waveNumber,
            int population,
            int managedMemoryMb,
            int nativeMemoryMb,
            DateTime capturedUtc)
        {
            GameDay = Math.Max(0, gameDay);
            Act = act ?? string.Empty;
            ActiveThreats = Math.Max(0, activeThreats);
            WaveNumber = Math.Max(0, waveNumber);
            Population = Math.Max(0, population);
            ManagedMemoryMb = Math.Max(0, managedMemoryMb);
            NativeMemoryMb = Math.Max(0, nativeMemoryMb);
            CapturedUtc = capturedUtc == default ? DateTime.MinValue : capturedUtc.ToUniversalTime();
        }

        public bool IsEmpty => CapturedUtc == DateTime.MinValue
            && GameDay == 0
            && string.IsNullOrEmpty(Act)
            && ActiveThreats == 0
            && WaveNumber == 0
            && Population == 0
            && ManagedMemoryMb == 0
            && NativeMemoryMb == 0;

        public static CrashContextSnapshot Empty => default;

        public bool Equals(CrashContextSnapshot other)
            => GameDay == other.GameDay
            && string.Equals(Act, other.Act, StringComparison.Ordinal)
            && ActiveThreats == other.ActiveThreats
            && WaveNumber == other.WaveNumber
            && Population == other.Population
            && ManagedMemoryMb == other.ManagedMemoryMb
            && NativeMemoryMb == other.NativeMemoryMb
            && CapturedUtc == other.CapturedUtc;

        public override bool Equals(object? obj)
            => obj is CrashContextSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = GameDay;
                hash = (hash * HashMultiplier) ^ (Act == null ? 0 : StringComparer.Ordinal.GetHashCode(Act));
                hash = (hash * HashMultiplier) ^ ActiveThreats;
                hash = (hash * HashMultiplier) ^ WaveNumber;
                hash = (hash * HashMultiplier) ^ Population;
                hash = (hash * HashMultiplier) ^ ManagedMemoryMb;
                hash = (hash * HashMultiplier) ^ NativeMemoryMb;
                hash = (hash * HashMultiplier) ^ CapturedUtc.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(CrashContextSnapshot left, CrashContextSnapshot right)
            => left.Equals(right);

        public static bool operator !=(CrashContextSnapshot left, CrashContextSnapshot right)
            => !left.Equals(right);
    }
}
