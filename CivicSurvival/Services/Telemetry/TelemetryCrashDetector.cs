using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Services.DevTools;
using UnityEngine;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Owns the error-capture, crash-detection, and session-end record paths.
    /// Hooked into Unity's log stream by the orchestrator; persists session_end
    /// summary; writes a clean-shutdown flag so the next launch can detect crashes.
    /// </summary>
    internal sealed class TelemetryCrashDetector : IDisposable
    {
        private static readonly LogContext Log = new("TelemetryCrashDetector");

        private readonly TelemetryConfig m_Config;
        private readonly TelemetryPersistence m_Persistence;
        private readonly TelemetryHttpClient m_Transport;
        private readonly TelemetryAuth m_Auth;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        [NonEntityIndex] private readonly Dictionary<int, float> m_ErrorDedupeMap = new(Engine.ErrorReporting.MAX_DEDUPE_KEYS);
        private readonly object m_ErrorDedupeLock = new();

        public TelemetryCrashDetector(
            TelemetryConfig config,
            TelemetryPersistence persistence,
            TelemetryHttpClient transport,
            TelemetryAuth auth,
            TelemetryRecorder recorder,
            string sessionId)
        {
            m_Config = config;
            m_Persistence = persistence;
            m_Transport = transport;
            m_Auth = auth;
            m_Recorder = recorder;
            m_SessionId = sessionId;
        }

        /// <summary>
        /// Subscribe to Unity's log stream. The hook exists exactly while DIAGNOSTICS is up
        /// (attached from StartDiagnostics, detached via <see cref="DetachUnityLogHook"/> from
        /// StopDiagnostics), so the diagnostics gate is enforced by the hook's lifetime. The
        /// detector INSTANCE itself is Online-scoped (built/disposed with the event pipeline), so
        /// hook detach is separated from <see cref="Dispose"/> to avoid retiring the instance on a
        /// diagnostics-off toggle while Online is still on.
        /// </summary>
        public void AttachUnityLogHook()
        {
            Application.logMessageReceived += OnUnityLogEntry;
        }

        /// <summary>Detach the Unity-log error hook. Idempotent — a delegate -= is a no-op when absent.</summary>
        public void DetachUnityLogHook()
        {
            Application.logMessageReceived -= OnUnityLogEntry;
        }

        public void Dispose()
        {
            DetachUnityLogHook();
        }

        private void OnUnityLogEntry(string condition, string stackTrace, LogType type)
        {
            // Error/Exception lines also feed the in-game error console (a separate sink from
            // the crash-detection pipeline below); writeToLog:false avoids re-entering Unity's
            // log stream we are currently handling.
            if (type == LogType.Error || type == LogType.Exception)
                ErrorReportService.LogError(FormatUnityError(condition, stackTrace), writeToLog: false);

            OnUnityLog(condition, stackTrace, type);
        }

        private static string FormatUnityError(string condition, string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
                return condition ?? string.Empty;

            return $"{condition}\n{stackTrace}";
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;

            if (!condition.Contains("CivicSurvival", StringComparison.Ordinal) &&
                !stackTrace.Contains("CivicSurvival", StringComparison.Ordinal)) return;

            // A vanilla Colossal.UI resource-handler load failure (e.g. a transient 404 for
            // coui://ui-mods/CivicSurvival.mjs when the bundle is momentarily unreadable on
            // the player's disk during a mid-session re-request) reaches us only because the
            // asset URL embeds the mod name — the stack is entirely Colossal.UI/UnityPlayer
            // with no CivicSurvival code frame, and the failing path is vanilla file IO
            // against a correctly-registered host we cannot influence. It is not a mod-code
            // error and self-heals, so report it as a warning rather than an error; a
            // recurring one would instead flag a genuinely broken install.
            var severity = IsVanillaResourceHandlerNoise(condition, stackTrace) ? "warning" : "error";

            var dedupeKey = condition.GetHashCode(StringComparison.Ordinal);
            var now = GetMonotonicSeconds();
            lock (m_ErrorDedupeLock)
            {
                if (m_ErrorDedupeMap.TryGetValue(dedupeKey, out var lastTime) &&
                    (now - lastTime) < Engine.ErrorReporting.DEDUPE_WINDOW_SECONDS)
                {
                    return;
                }

                EvictErrorDedupeIfFullLocked();

                m_ErrorDedupeMap[dedupeKey] = now;
            }

            var errorEvent = new TelemetryEvent
            {
                EventId = Guid.NewGuid().ToString("D"),
                SessionId = m_SessionId,
                Type = Error.Report,
                Timestamp = DateTime.UtcNow,
                Data = new ErrorReportData
                {
                    Severity = severity,
                    // Mask OS user-profile paths / user name in the condition and
                    // stack trace before they leave in telemetry. Source is a CivicSurvival
                    // type/method name (not PII), extracted from the unredacted trace.
                    Message = PiiRedactor.Redact(condition),
                    StackTrace = PiiRedactor.Redact(stackTrace),
                    Source = ExtractSource(stackTrace) ?? ""
                }
            };

            SendImmediateEvent(errorEvent);
        }

        /// <summary>
        /// Persist an event to disk crash-safely, then attempt immediate HTTP send.
        /// Shared by the Unity-log hook and DetectPreviousCrash.
        /// </summary>
        public void SendImmediateEvent(TelemetryEvent evt)
        {
            if (m_Persistence == null) return;

            var segmentPath = m_Persistence.AppendToDisk(evt);

            if (!m_Config.FileOnlyMode && m_Transport != null && m_Auth != null)
            {
                var payload = new TelemetryPayload
                {
                    SessionId = m_SessionId,
                    // Durable identity must ride the envelope so the session row is linked to the
                    // player on its FIRST event. The crash breadcrumb is the first (often only)
                    // event of a recovery boot; without this the session is upserted player_id=NULL
                    // and the crash report resolves to "(anon)" even for a player with a nickname.
                    // The batch path already carries it (TelemetrySendPipeline.BuildPayloadJson).
                    PlayerId = m_Auth.PlayerId,
                    ModVersion = Mod.VERSION,
                    GameVersion = Application.version,
                    Timestamp = DateTime.UtcNow,
                    Events = new List<TelemetryEvent> { evt }
                };

                m_Transport.TrySendImmediate(payload, m_Auth.AuthToken, () => m_Persistence.DeletePendingSegment(segmentPath));
            }
        }

        public void DetectPreviousCrash(bool hadRecoverablePendingEvents)
        {
            var flagPath = System.IO.Path.Combine(m_Config.LogsDirectory, ModPaths.CleanShutdownFile);

            bool wasClean;
            try
            {
                wasClean = System.IO.File.Exists(flagPath);
                if (wasClean)
                    System.IO.File.Delete(flagPath);
            }
#pragma warning disable CIVIC052 // Best-effort flag check — treat failure as "no flag"
            catch { wasClean = false; }
#pragma warning restore CIVIC052

            if (wasClean)
            {
                NativeCrashBreadcrumb.Clear();
                CrashContextProvider.ClearPersisted();
                return;
            }

            NativeCrashBreadcrumbRecord recoveredRecord;
            var hasNativeBreadcrumb = NativeCrashBreadcrumb.TryConsume(out recoveredRecord);
            var hasPersistedContext = CrashContextProvider.TryReadPersisted(out var persistedContext, out var persistedModVersion, out var persistedSessionId, out var persistedRecentMarkers, out var persistedPhase, out var persistedHeartbeatUtc);
            if (!hasNativeBreadcrumb && !hadRecoverablePendingEvents && !hasPersistedContext)
            {
                return;
            }

            if (!hasNativeBreadcrumb)
            {
                // No marker file → take the crashing build/session identity from the persisted
                // context file (written by CrashContextProvider during the crashing run).
                recoveredRecord = new NativeCrashBreadcrumbRecord(
                    NativeCrashMarkers.Unknown,
                    hasPersistedContext ? persistedContext : CrashContextSnapshot.Empty,
                    DateTime.MinValue,
                    persistedModVersion,
                    persistedSessionId,
                    // Phase sequence mirrored into the context file (the marker file is absent on an
                    // unmarked unclean shutdown) — the only surviving phase signal for these crashes.
                    recentMarkers: persistedRecentMarkers,
                    spatialHashCount: 0,
                    spatialHashCapacity: 0,
                    buildingCacheLength: 0,
                    lastJob: string.Empty,
                    // No marker file in this recovery process → CrashScalars is empty (in-memory does
                    // not survive the restart); the path, when present, comes from the marker file itself.
                    lastFilePath: string.Empty);
            }

            var marker = recoveredRecord.Marker;
            // The breadcrumb embeds the context captured when its marker first entered the
            // active set. The active-set dedup (NativeCrashBreadcrumb PERF-LOCK) keeps the
            // per-update path write-free, so for a wave-resident marker (e.g. ThreatMovement)
            // that context is frozen at first-entry — often the empty session-start snapshot.
            // CrashContextProvider persists a fresher snapshot every telemetry pulse; prefer it
            // when the embedded one is empty or staler, so a resident-marker crash still reports
            // real game state instead of all-zero context.
            var context = recoveredRecord.Context;
            if (hasPersistedContext
                && (context.IsEmpty || persistedContext.CapturedUtc > context.CapturedUtc))
            {
                context = persistedContext;
            }

            // Phase sequence: prefer the marker file's ring; fall back to the ring mirrored into the
            // persisted context (the only artifact that survives an UNMARKED unclean shutdown).
            // Decoupled from the snapshot freshness arbitration above so it is not dropped when the
            // embedded marker snapshot wins.
            var recentMarkers = !string.IsNullOrEmpty(recoveredRecord.RecentMarkers)
                ? recoveredRecord.RecentMarkers
                : persistedRecentMarkers;
            var minidump = FindLatestMinidump();

            // Parse the faulting module/offset/code out of the freshest minidump locally. The dump
            // itself never leaves the machine — only this attribution fact does. The module name is
            // file-name-only (path stripped in the reader) and redacted here; the result is null when
            // no dump exists, the dump has no exception stream, or it is not parseable. This is the
            // signal that answers our-code-vs-vanilla without shipping a 30 MB binary.
            var fault = MinidumpStackReader.TryReadFault(minidump.Path);

            // Raw attribution facts — NOT a verdict. How stale the recovered marker is, and how
            // fresh the freshest dump is, both relative to this recovery moment. A large breadcrumb
            // age together with a non-fresh dump means a resident marker (e.g. Runtime.InSimulation)
            // outlived its crash and rode into an unrelated relaunch — the stuck-marker phantom. We
            // surface the numbers and let the reader form a probabilistic label; the mod never
            // asserts a hard attribution that could itself be wrong. Null when the timestamp is absent.
            var nowUtc = DateTime.UtcNow;
            float? breadcrumbAgeSeconds = recoveredRecord.CapturedUtc == DateTime.MinValue
                ? null
                : (float)Math.Max(0.0, (nowUtc - recoveredRecord.CapturedUtc).TotalSeconds);
            float? minidumpFreshnessSeconds = minidump.Utc == DateTime.MinValue
                ? null
                : (float)Math.Max(0.0, (nowUtc - minidump.Utc).TotalSeconds);

            // Lifecycle phase + its age, recovered from the context file: which phase the crashing
            // session was in, and how long before recovery our code last confirmed it live. With the
            // fault code this is the phase × fault-code classification input (a Loading/Saving 0x0517A7ED
            // is a legitimate long sync op; an ActiveSim one is a genuine in-game freeze). Raw, no verdict.
            float? phaseAgeSeconds = null;
            if (!string.IsNullOrEmpty(persistedHeartbeatUtc)
                && DateTime.TryParse(persistedHeartbeatUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var heartbeatUtc))
            {
                phaseAgeSeconds = (float)Math.Max(0.0, (nowUtc - heartbeatUtc).TotalSeconds);
            }

            Log.Warn($" Previous session ended uncleanly with native crash marker: {marker}");

            var breadcrumbEvent = new TelemetryEvent
            {
                EventId = Guid.NewGuid().ToString("D"),
                SessionId = m_SessionId,
                Type = Diagnostics.NativeCrashBreadcrumb,
                Timestamp = nowUtc,
                Data = new DiagnosticsNativeCrashBreadcrumbData
                {
                    Marker = marker,
                    GameDay = context.GameDay,
                    Act = context.Act,
                    ActiveThreats = context.ActiveThreats,
                    WaveNumber = context.WaveNumber,
                    Population = context.Population,
                    ManagedMemoryMb = context.ManagedMemoryMb,
                    NativeMemoryMb = context.NativeMemoryMb,
                    ContextUtc = FormatUtcOrNull(context.CapturedUtc),
                    BreadcrumbUtc = FormatUtcOrNull(recoveredRecord.CapturedUtc),
                    MinidumpId = minidump.Id,
                    MinidumpUtc = FormatUtcOrNull(minidump.Utc),
                    CrashModVersion = string.IsNullOrEmpty(recoveredRecord.CrashModVersion) ? null : recoveredRecord.CrashModVersion,
                    CrashedSessionId = string.IsNullOrEmpty(recoveredRecord.CrashedSessionId) ? null : recoveredRecord.CrashedSessionId,
                    RecentMarkers = string.IsNullOrEmpty(recentMarkers) ? null : recentMarkers,
                    SpatialHashCount = recoveredRecord.SpatialHashCount,
                    SpatialHashCapacity = recoveredRecord.SpatialHashCapacity,
                    BuildingCacheLength = recoveredRecord.BuildingCacheLength,
                    LastJob = string.IsNullOrEmpty(recoveredRecord.LastJob) ? null : recoveredRecord.LastJob,
                    LastFilePath = string.IsNullOrEmpty(recoveredRecord.LastFilePath) ? null : PiiRedactor.Redact(recoveredRecord.LastFilePath),
                    BreadcrumbAgeSeconds = breadcrumbAgeSeconds,
                    MinidumpFreshnessSeconds = minidumpFreshnessSeconds,
                    FaultingModule = fault.Found ? PiiRedactor.Redact(fault.Module) : null,
                    FaultingOffset = fault.Found ? "0x" + fault.Offset.ToString("x", CultureInfo.InvariantCulture) : null,
                    ExceptionCode = fault.Found ? "0x" + fault.ExceptionCode.ToString("X8", CultureInfo.InvariantCulture) : null,
                    Phase = string.IsNullOrEmpty(persistedPhase) ? null : persistedPhase,
                    PhaseAgeSeconds = phaseAgeSeconds
                }
            };

            // A crash breadcrumb is a one-shot critical signal: route it through the
            // immediate path (disk-safe append + instant HTTP) like error.report, so it
            // does not depend on the 5-minute batch timer. A short detector session
            // would otherwise persist it back to disk and never send it.
            SendImmediateEvent(breadcrumbEvent);

            // Second, independent signal: the anonymous crash counter. The detailed breadcrumb
            // above rides the diagnostics opt-in (SendImmediateEvent self-gates on FileOnlyMode),
            // so it reaches only that subset. This minimal version+marker counter fires whenever
            // ONLINE is on — the whole-Online-audience crash-rate signal — carrying no session,
            // player, or context. Use the crashing build's version when known (else the current).
            if (m_Config.OnlineEnabled && m_Transport != null)
            {
                var crashVersion = string.IsNullOrEmpty(recoveredRecord.CrashModVersion)
                    ? Mod.VERSION
                    : recoveredRecord.CrashModVersion;
                // Raw exception code (no verdict) so the whole-audience aggregate can be sliced
                // ANR vs real crash; empty when no dump was parsed (kill / power / managed).
                var crashRateExceptionCode = fault.Found
                    ? "0x" + fault.ExceptionCode.ToString("X8", CultureInfo.InvariantCulture)
                    : string.Empty;
                // Phase (whitelisted enum, GDPR-safe) so the whole-audience aggregate can split a
                // load/save false-ANR from an in-game freeze. Empty when no heartbeat was recovered.
                m_Transport.SendCrashRate(crashVersion, marker, crashRateExceptionCode, persistedPhase ?? string.Empty);
            }

            // Marker file is already consumed (TryConsume → Clear); drop the persisted context
            // file too so this crash's snapshot cannot bleed into a later recovery cycle if the
            // relaunch session itself crashes before its first telemetry pulse rewrites it.
            CrashContextProvider.ClearPersisted();
        }

        public void WriteCleanShutdownFlag()
        {
            try
            {
                var flagPath = System.IO.Path.Combine(m_Config.LogsDirectory, ModPaths.CleanShutdownFile);
                // Direct in-place write (1 syscall) instead of atomic temp+replace (3-4): shutdown-time
                // flag in the OneDrive/AV ANR zone. A torn flag just reads as "no clean shutdown" next
                // launch (the detector treats a missing/unmatched flag as a possible crash anyway).
                AtomicFileWriter.WriteAllTextDirect(flagPath, m_SessionId);
                NativeCrashBreadcrumb.Clear();
                CrashContextProvider.ClearPersisted();
            }
#pragma warning disable CIVIC052 // Best-effort flag write
            catch (Exception ex)
            {
                Log.Warn($" Failed to write clean shutdown flag: {ex}");
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// Record a session_end event. Orchestrator gathers exit reason and runtime
        /// state, passes them in as value-typed inputs.
        /// </summary>
        public void RecordSessionEnd(
            string exitReason,
            float totalPlaytimeSeconds,
            int gameDay,
            string act,
            int population,
            long money)
        {
            if (m_Recorder == null) return;

            m_Recorder.Record(m_SessionId, Session.SessionEnd, new SessionSessionEndData
            {
                ExitReason = exitReason,
                TotalPlaytimeSeconds = (float)Math.Round(totalPlaytimeSeconds, 1),
                GameDay = gameDay,
                Act = act,
                Population = population,
                Money = money
            });

            Log.Info($" Session end: {exitReason}, {totalPlaytimeSeconds:F0}s, day {gameDay}, {act}");
        }

        /// <summary>
        /// True when the Unity error line is a vanilla Colossal.UI resource-handler
        /// load failure (a coui:// 404 / Not Found) that matched our attribution gate
        /// only because the failing URL contains the mod name. Genuine mod-side errors
        /// carry a CivicSurvival frame in the stack (C# code frames, or a JS frame
        /// referencing CivicSurvival.mjs for React errors); a pure vanilla resolver
        /// failure does not. Such 404s are transient file-load failures (the host is
        /// registered, the file was momentarily unreadable) that we cannot fix and must
        /// not report as mod errors.
        /// </summary>
        private static bool IsVanillaResourceHandlerNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition)) return false;

            bool isResourceLoadFailure =
                condition.Contains("ResourceHandler", StringComparison.Ordinal) &&
                (condition.Contains("404", StringComparison.Ordinal) ||
                 condition.Contains("Not Found", StringComparison.Ordinal));
            if (!isResourceLoadFailure) return false;

            return string.IsNullOrEmpty(stackTrace) ||
                   !stackTrace.Contains("CivicSurvival", StringComparison.Ordinal);
        }

        private static string? ExtractSource(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return null;

            var atIndex = stackTrace.IndexOf("at CivicSurvival.", StringComparison.Ordinal);
            if (atIndex < 0) return null;

            var start = atIndex + 3;
            var end = stackTrace.IndexOf('(', start);
            if (end < 0) return null;

            var fullName = stackTrace.Substring(start, end - start);
            var lastDot = fullName.LastIndexOf('.');
            if (lastDot < 0) return fullName;

            var secondLastDot = fullName.LastIndexOf('.', lastDot - 1);
            return secondLastDot < 0 ? fullName : fullName.Substring(secondLastDot + 1);
        }

        [CallerHoldsLock(nameof(m_ErrorDedupeLock))]
        private void EvictErrorDedupeIfFullLocked()
        {
            if (m_ErrorDedupeMap.Count < Engine.ErrorReporting.MAX_DEDUPE_KEYS) return;

            var oldestKey = 0;
            var oldestTime = float.MaxValue;
            foreach (var kvp in m_ErrorDedupeMap)
            {
                if (kvp.Value < oldestTime)
                {
                    oldestTime = kvp.Value;
                    oldestKey = kvp.Key;
                }
            }

            m_ErrorDedupeMap.Remove(oldestKey);
        }

        private static float GetMonotonicSeconds()
        {
            double frequency = Stopwatch.Frequency;
            if (frequency <= 0.0)
                return 0f;

            return (float)(Stopwatch.GetTimestamp() / frequency);
        }

        private static string? FormatUtcOrNull(DateTime value)
            => value == DateTime.MinValue ? null : value.ToUniversalTime().ToString("O");

        private static NativeMinidumpInfo FindLatestMinidump()
        {
            try
            {
                var reportsPath = Path.Combine(ModPaths.GameDataRoot, ".cache", "backtrace", "crashpad", "reports");
                var directory = new DirectoryInfo(reportsPath);
                if (!directory.Exists)
                    return NativeMinidumpInfo.Empty;

                FileInfo? latest = null;
                foreach (var file in directory.EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly))
                {
                    if (latest == null || file.CreationTimeUtc > latest.CreationTimeUtc)
                        latest = file;
                }

                if (latest == null)
                    return NativeMinidumpInfo.Empty;

                return new NativeMinidumpInfo(
                    Path.GetFileNameWithoutExtension(latest.Name),
                    latest.CreationTimeUtc,
                    latest.FullName);
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Minidump scan failed: {ex.Message}");
                return NativeMinidumpInfo.Empty;
            }
        }

        private readonly struct NativeMinidumpInfo
        {
            public readonly string? Id;
            public readonly DateTime Utc;
            public readonly string? Path;

            public NativeMinidumpInfo(string? id, DateTime utc, string? path)
            {
                Id = string.IsNullOrWhiteSpace(id) ? null : id;
                Utc = utc == default ? DateTime.MinValue : utc.ToUniversalTime();
                Path = string.IsNullOrWhiteSpace(path) ? null : path;
            }

            public static NativeMinidumpInfo Empty => default;
        }
    }
}
