using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Services.Telemetry;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// Service for collecting error logs and submitting bug reports.
    /// Reports are sent via TelemetryService to the central server.
    /// </summary>
    public static class ErrorReportService
    {
        private static readonly LogContext Log = new("ErrorReportService");
        private const int MAX_LOG_LINES = Engine.ErrorReporting.MAX_LOG_LINES;
        private const int MAX_RECENT_ERRORS = Engine.ErrorReporting.MAX_RECENT_ERRORS;
        private const int MAX_STACK_TRACE_LINES = Engine.ErrorReporting.MAX_STACK_TRACE_LINES;
        private const int MAX_LOG_TAIL_BYTES = 64 * 1024;
        private const int MAX_USER_COMMENT_CHARS = 4096;
        private const int MAX_REPORT_CHARS = 128 * 1024;
        // Crash-dump upload runs off-thread; a ~30 MB read + zip + multipart send can take seconds.
        private const int CRASH_DUMP_TIMEOUT_MS = 60000;
        // Upper bound on the dump list surfaced to the UI (crashpad rotation is not aggressive,
        // dumps live for days — a few entries, not hundreds).
        private const int MAX_CRASH_DUMPS_LISTED = 20;
        // Each dump is ~33-50 MB; cap a single submit so the player cannot queue, say, 5x50 MB at once.
        private const int MAX_CRASH_DUMPS_PER_SUBMIT = 3;
        private const long BYTES_PER_MB = 1024L * 1024L;
        private static readonly char[] s_PathSeparators = { '/', '\\' };

        // MED-3 FIX: LinkedList for O(1) AddLast/RemoveFirst (was List with O(N) RemoveAt(0))
        // Note: Queue<T> is ambiguous between System.dll and mscorlib on .NET 4.8
        private static readonly LinkedList<string> s_RecentErrors = new LinkedList<string>();
        private static readonly Dictionary<string, DateTime> s_LastLoggedByKey = new Dictionary<string, DateTime>();
        private static readonly object s_Lock = new object();

        public enum ManualReportResult
        {
            Unknown = 0,
            Sent = 1,
            TelemetryUnavailable = 2,
            TelemetryDisabled = 3,
            Failed = 4,
            NoDump = 5
        }

        /// <summary>
        /// Log an error for potential bug report.
        /// Recorded in a bounded ring for the bug-report flow.
        /// </summary>
        public static void LogError(string? message, Exception? ex = null, bool writeToLog = true)
        {

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var entry = $"[{timestamp}] {message}";

            if (ex != null)
            {
                entry += $"\n  Exception: {ex.GetType().Name}: {ex.Message}";
                if (ex.StackTrace != null)
                {
                    // Limit stack trace to first 5 lines
                    var lines = ex.StackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var limitedStack = string.Join("\n", lines, 0, Math.Min(MAX_STACK_TRACE_LINES, lines.Length));
                    entry += $"\n  Stack: {limitedStack}";
                }
            }

            bool shouldWriteLog;
            lock (s_Lock)
            {
                string key = Truncate(message ?? string.Empty, 128);
                DateTime now = DateTime.UtcNow;
                shouldWriteLog = !s_LastLoggedByKey.TryGetValue(key, out var last)
                    || (now - last).TotalSeconds >= Engine.ErrorReporting.DEDUPE_WINDOW_SECONDS;

                s_RecentErrors.AddLast(entry);
                // Keep the recent-report ring complete; dedupe only gates external log spam.
                while (s_RecentErrors.Count > MAX_RECENT_ERRORS)
                    s_RecentErrors.RemoveFirst();

                if (shouldWriteLog)
                {
                    if (s_LastLoggedByKey.Count >= Engine.ErrorReporting.MAX_DEDUPE_KEYS)
                        EvictOldestDedupeKey();

                    s_LastLoggedByKey[key] = now;
                }
            }

            if (writeToLog && shouldWriteLog)
            {
                Log.Error(message ?? string.Empty);
                if (ex != null)
                    Log.Error(ex.ToString());
            }
        }

        /// <summary>
        /// Generate a bug report with system info and recent logs.
        /// </summary>
        public static string GenerateReport()
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("=== Civic Survival Bug Report ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // System info
            sb.AppendLine("--- System Info ---");
            sb.AppendLine($"Mod Version: {Mod.VERSION}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Platform: {Application.platform}");
            sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"Graphics: {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($"Memory: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine();

            // Settings
            sb.AppendLine("--- Mod Settings ---");
#pragma warning disable CIVIC169 // Debug report: intentional TryGet per call
            var modSettings = ServiceRegistry.TryGet<ModSettings>();
#pragma warning restore CIVIC169
            if (modSettings != null)
            {
                sb.AppendLine($"Difficulty: {modSettings.CurrentPreset}");
                sb.AppendLine($"LegalImportMW: {modSettings.LegalImportMW}");
                sb.AppendLine($"LegalExportMW: {modSettings.LegalExportMW}");
                sb.AppendLine($"ConstructionDelay: {modSettings.ConstructionDelayEnabled}");
                sb.AppendLine($"RandomDisasters: {modSettings.RandomDisastersEnabled}");
                sb.AppendLine($"WinterMultiplier: {modSettings.WinterMultiplierEnabled}");
                // CascadeEffect removed - vanilla handles via EfficiencyFactor.ElectricitySupply
                sb.AppendLine($"BackupPower: {modSettings.BackupPowerEnabled}");
                sb.AppendLine($"AirAttacks: {modSettings.AirAttacks}");
            }
            else
            {
                sb.AppendLine("Settings not available");
            }
            sb.AppendLine();

            // Recent errors from memory
            sb.AppendLine("--- Recent Errors (in-memory) ---");
            List<string> recentErrors;
            lock (s_Lock)
            {
                recentErrors = new List<string>(s_RecentErrors);
            }

            if (recentErrors.Count == 0)
            {
                sb.AppendLine("No errors recorded in current session.");
            }
            else
            {
                foreach (var error in recentErrors)
                    sb.AppendLine(error);
            }
            sb.AppendLine();

            // Try to read last lines from log file
            sb.AppendLine("--- Recent Log File Entries ---");
            try
            {
                var logPath = GetLogFilePath();
                if (File.Exists(logPath))
                {
                    var lines = ReadLastLines(logPath, MAX_LOG_LINES);
                    foreach (var line in lines)
                    {
                        sb.AppendLine(line);
                    }
                }
                else
                {
                    sb.AppendLine($"Log file not found: {logPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to read log file for bug report", ex);
                sb.AppendLine($"Failed to read log file: {ex.GetType().Name}: {SanitizeForReport(ex.Message, 512)}");
            }

            // Previous session's log (rotated at startup before the logger truncated CivicSurvival.log).
            // After a crash/freeze + restart the current log is the fresh session — the cause lives here.
            sb.AppendLine();
            sb.AppendLine("--- Previous Session Log Entries ---");
            try
            {
                var prevPath = GetPrevLogFilePath();
                if (File.Exists(prevPath))
                {
                    var lines = ReadLastLines(prevPath, MAX_LOG_LINES);
                    foreach (var line in lines)
                        sb.AppendLine(line);
                }
                else
                {
                    sb.AppendLine("No previous-session log (first run, or clean shutdown).");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to read previous-session log for bug report", ex);
                sb.AppendLine($"Failed to read previous-session log: {ex.GetType().Name}: {SanitizeForReport(ex.Message, 512)}");
            }

            // Mask OS user-profile paths / user name in the whole report (log tail +
            // in-memory errors) before it can leave in telemetry ReportContent.
            return Truncate(PiiRedactor.Redact(sb.ToString()), MAX_REPORT_CHARS);
        }

        /// <summary>
        /// Copy bug report to clipboard and save to file.
        /// </summary>
        public static bool CopyToClipboard()
        {
            try
            {
                var report = GenerateReport();

                // Save to file (always works)
                var reportPath = GetReportFilePath();
                AtomicFileWriter.WriteAllText(reportPath, report);
                Log.Info($"Bug report saved to: {reportPath}");

                return TryCopyToSystemClipboard(report);
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to save report", ex);
                return false;
            }
        }

        /// <summary>
        /// Submit a manual bug report via TelemetryService.
        /// Returns true if telemetry is enabled and report was queued.
        /// </summary>
        public static ManualReportResult SubmitManualReport(string userComment = "")
        {
            var telemetry = TelemetryService.Instance;
            if (telemetry == null)
            {
                Log.Warn("Cannot submit report: TelemetryService not available");
                return ManualReportResult.TelemetryUnavailable;
            }

#pragma warning disable CIVIC169 // Manual report submit: rare call path
            var settings = ServiceRegistry.TryGet<ModSettings>();
#pragma warning restore CIVIC169
            if (settings == null || !settings.TelemetryEnabled)
            {
                Log.Warn("Cannot submit report: Telemetry is disabled");
                return ManualReportResult.TelemetryDisabled;
            }

            try
            {
                var report = GenerateReport();
                telemetry.RecordManualReport(
                    SanitizeForReport(userComment, MAX_USER_COMMENT_CHARS),
                    Truncate(report, MAX_REPORT_CHARS));
                return ManualReportResult.Sent;
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to submit manual report", ex);
                return ManualReportResult.Failed;
            }
        }

        /// <summary>
        /// Submit the vanilla Modding.log (active playset + enabled mods + versions + load order) via
        /// TelemetryService, for mod-conflict triage. Separate from the diagnostics report — most "bug"
        /// reports are environment/mod conflicts invisible without the player's mod list.
        /// </summary>
        public static ManualReportResult SubmitModList(string userComment = "")
        {
            var telemetry = TelemetryService.Instance;
            if (telemetry == null)
            {
                Log.Warn("Cannot submit mod list: TelemetryService not available");
                return ManualReportResult.TelemetryUnavailable;
            }

#pragma warning disable CIVIC169 // Manual submit: rare call path
            var settings = ServiceRegistry.TryGet<ModSettings>();
#pragma warning restore CIVIC169
            if (settings == null || !settings.TelemetryEnabled)
            {
                Log.Warn("Cannot submit mod list: Telemetry is disabled");
                return ManualReportResult.TelemetryDisabled;
            }

            try
            {
                var modList = GenerateModListReport();
                telemetry.RecordManualReport(
                    SanitizeForReport("[MOD LIST] " + userComment, MAX_USER_COMMENT_CHARS),
                    Truncate(modList, MAX_REPORT_CHARS));
                return ManualReportResult.Sent;
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to submit mod list", ex);
                return ManualReportResult.Failed;
            }
        }

        /// <summary>
        /// Newest-first JSON array (<c>CrashDumpEntry[]</c>) of available native crash dumps for the
        /// bug-report tab, so the player can see each dump's time and size and choose which to send.
        /// Cheap directory stat — safe on the main thread; returns <c>"[]"</c> on any failure or when
        /// the crashpad reports dir is absent.
        /// </summary>
        public static string GetCrashDumpListJson()
        {
            try
            {
                var dir = GetCrashpadReportsDir();
                if (!dir.Exists) return "[]";

                var files = new List<FileInfo>();
                foreach (var file in dir.EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly))
                    files.Add(file);

                files.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));

                var sb = new StringBuilder(256);
                sb.Append('[');
                int count = 0;
                foreach (var file in files)
                {
                    if (count >= MAX_CRASH_DUMPS_LISTED) break;
                    if (count > 0) sb.Append(',');

                    var entry = new CrashDumpEntry
                    {
                        Name = file.Name,
                        SizeMb = (float)Math.Round(file.Length / (double)BYTES_PER_MB, 1),
                        TimeText = file.CreationTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    };
                    entry.WriteTo(sb);
                    count++;
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Crash dump list scan failed: {ex.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// Send the player-selected native crash dumps (.dmp) to the server, which forwards them to the
        /// dedicated Discord channel (NOT stored in our DB). The dump is the only carrier of the faulting
        /// stack / the parked main-thread stack for an ANR. Player-initiated; each read + zip + upload
        /// runs on a background thread — never the main thread (a ~30 MB read + compression would freeze
        /// the game and ironically trip the 15 s ANR watchdog). Selected dumps are resolved + validated
        /// synchronously (cheap dir stat) so "no dump" reports without a cross-thread status marshal, then
        /// uploaded one after another on a single worker. <paramref name="selectedNames"/> are bare file
        /// names from <see cref="GetCrashDumpListJson"/>; an empty list falls back to the freshest dump.
        /// At most <see cref="MAX_CRASH_DUMPS_PER_SUBMIT"/> are sent. Returns Sent optimistically once the
        /// work is queued (same contract as the manual report); failures of the async leg are logged.
        /// </summary>
        public static ManualReportResult SubmitCrashDumps(IReadOnlyList<string>? selectedNames, string userComment = "")
        {
            var telemetry = TelemetryService.Instance;
            if (telemetry == null)
            {
                Log.Warn("Cannot send crash dump: TelemetryService not available");
                return ManualReportResult.TelemetryUnavailable;
            }

#pragma warning disable CIVIC169 // Manual submit: rare call path
            var settings = ServiceRegistry.TryGet<ModSettings>();
#pragma warning restore CIVIC169
            if (settings == null || !settings.TelemetryEnabled)
            {
                Log.Warn("Cannot send crash dump: Telemetry is disabled");
                return ManualReportResult.TelemetryDisabled;
            }

            var dumps = ResolveSelectedDumps(selectedNames);
            if (dumps.Count == 0)
            {
                Log.Info("No native crash dump found to send");
                return ManualReportResult.NoDump;
            }

            if (!telemetry.TryGetCrashDumpUpload(out var serverUrl, out var authToken, out var sessionId))
            {
                Log.Warn("Cannot send crash dump: telemetry upload unavailable");
                return ManualReportResult.TelemetryUnavailable;
            }

            string comment = SanitizeForReport("[CRASH DUMP] " + userComment, MAX_USER_COMMENT_CHARS);
            string modVersion = Mod.VERSION;

            // Heavy work OFF the main thread (read + zip + multipart upload). Fire-and-forget: the
            // optimistic Sent below means "accepted for sending". A background thread must not write a
            // UI binding, so the async result is logged only. Dumps go out sequentially on one worker so
            // several 30-50 MB uploads do not run in parallel.
#pragma warning disable CIVIC029 // Guarded by Mod.IsUnloading inside the worker
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
#pragma warning restore CIVIC029
            {
                foreach (var dump in dumps)
                {
                    if (Mod.IsUnloading) return;
                    SendOneCrashDump(dump.Path, dump.Name, serverUrl, authToken, sessionId, comment, modVersion);
                }
            });

            return ManualReportResult.Sent;
        }

        /// <summary>
        /// Resolve the player-selected dump names to existing files in the crashpad reports dir. Names
        /// are validated as bare file names (no path separators / traversal) so a crafted payload cannot
        /// escape the dir; missing files (crashpad rotated one away between listing and the click) are
        /// skipped, not fatal. An empty/null selection falls back to the single freshest dump. Capped at
        /// <see cref="MAX_CRASH_DUMPS_PER_SUBMIT"/>.
        /// </summary>
        private static List<CrashDumpFile> ResolveSelectedDumps(IReadOnlyList<string>? selectedNames)
        {
            var result = new List<CrashDumpFile>();
            try
            {
                var dir = GetCrashpadReportsDir();
                if (!dir.Exists) return result;

                if (selectedNames == null || selectedNames.Count == 0)
                {
                    var latest = SelectLatestCrashDump();
                    if (latest != null) result.Add(latest.Value);
                    return result;
                }

                foreach (var rawName in selectedNames)
                {
                    if (result.Count >= MAX_CRASH_DUMPS_PER_SUBMIT)
                    {
                        Log.Warn($"Crash dump selection capped at {MAX_CRASH_DUMPS_PER_SUBMIT}; ignoring extras");
                        break;
                    }

                    string name = rawName?.Trim() ?? string.Empty;
                    if (name.Length == 0) continue;

                    bool isBareDmpName =
                        name.IndexOfAny(s_PathSeparators) < 0 &&
                        name.IndexOf("..", StringComparison.Ordinal) < 0 &&
                        Path.GetFileName(name) == name &&
                        name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase);
                    if (!isBareDmpName)
                    {
                        Log.Warn("Rejected invalid crash dump name");
                        continue;
                    }

                    string full = Path.Combine(dir.FullName, name);
                    if (!File.Exists(full))
                    {
                        Log.Info("Selected crash dump no longer exists; skipping");
                        continue;
                    }

                    bool duplicate = false;
                    foreach (var existing in result)
                    {
                        if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate) result.Add(new CrashDumpFile(full, name));
                }
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Crash dump resolve failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Zip + multipart-upload a single dump. Runs on a background thread; deletes its temp zip.
        /// </summary>
        private static void SendOneCrashDump(
            string dumpPath, string dumpName, string serverUrl, string authToken,
            string sessionId, string comment, string modVersion)
        {
            string? zipPath = null;
            try
            {
                if (Mod.IsUnloading) return;
                zipPath = ZipCrashDumpToTemp(dumpPath);
                if (zipPath == null) return;
                if (Mod.IsUnloading) return;

                var fields = new Dictionary<string, string>
                {
                    ["SessionId"] = sessionId,
                    ["ModVersion"] = modVersion,
                    ["UserComment"] = comment,
                    ["DumpName"] = dumpName,
                };

                var result = HttpUtils.PostMultipartFile(
                    serverUrl + "/crash-dump",
                    authToken,
                    zipPath,
                    fileFieldName: "dump",
                    fileName: Path.GetFileNameWithoutExtension(dumpName) + ".zip",
                    fileContentType: "application/zip",
                    formFields: fields,
                    timeoutMs: CRASH_DUMP_TIMEOUT_MS);

                if (result.Success)
                    Log.Info($"Crash dump sent ({new FileInfo(zipPath).Length / 1024} KB zipped)");
                else
                    Log.Warn($"Crash dump send failed: {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                Log.Exception("Crash dump send failed", ex);
            }
            finally
            {
                if (zipPath != null)
                {
                    try { File.Delete(zipPath); }
#pragma warning disable CIVIC052 // best-effort temp cleanup
                    catch (Exception ex) { if (Log.IsDebugEnabled) Log.Debug($"Temp zip cleanup failed: {ex.Message}"); }
#pragma warning restore CIVIC052
                }
            }
        }

        private readonly struct CrashDumpFile
        {
            public readonly string Path;
            public readonly string Name;
            public CrashDumpFile(string path, string name)
            {
                Path = path;
                Name = name;
            }
        }

        /// <summary>
        /// Newest <c>.dmp</c> in the vanilla crashpad reports dir by creation time, or null if none.
        /// On the relaunch-after-crash path this is the incident dump; on the static button it is the
        /// freshest available (the dedicated relaunch prompt — a follow-up — binds to the exact incident).
        /// Cheap directory stat: safe to call on the main thread.
        /// </summary>
        private static CrashDumpFile? SelectLatestCrashDump()
        {
            try
            {
                var dir = GetCrashpadReportsDir();
                if (!dir.Exists) return null;

                FileInfo? latest = null;
                foreach (var file in dir.EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly))
                {
                    if (latest == null || file.CreationTimeUtc > latest.CreationTimeUtc)
                        latest = file;
                }
                if (latest == null) return null;
                return new CrashDumpFile(latest.FullName, latest.Name);
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Crash dump scan failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The vanilla crashpad reports directory (<c>.cache/backtrace/crashpad/reports</c>) where the
        /// native dumps (.dmp) land. Single source for both the list scan and the send path.
        /// </summary>
        private static DirectoryInfo GetCrashpadReportsDir()
        {
            var reportsDir = Path.Combine(ModPaths.GameDataRoot, ".cache", "backtrace", "crashpad", "reports");
            return new DirectoryInfo(reportsDir);
        }

        /// <summary>
        /// Zip the dump into a temp file (system temp — NOT the OneDrive/AV LocalLow zone) so the
        /// upload is ~8-12 MB instead of ~30 MB. Returns the temp zip path, or null on failure. Caller
        /// deletes the temp file. Runs on a background thread.
        /// </summary>
        private static string? ZipCrashDumpToTemp(string dumpPath)
        {
            try
            {
                string zipPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "civic_crashdump_" + Guid.NewGuid().ToString("N") + ".zip");

                using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry(System.IO.Path.GetFileName(dumpPath), System.IO.Compression.CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var source = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    source.CopyTo(entryStream);
                }
                return zipPath;
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to zip crash dump", ex);
                return null;
            }
        }

        /// <summary>
        /// Read the vanilla Modding.log whole (small, ~5 KB) with a header, PII-redacted. The mod-load
        /// log lists the active playset, enabled mods + versions + Paradox IDs, and load order/timings —
        /// the signal for mod-conflict triage.
        /// </summary>
        private static string GenerateModListReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Civic Survival Mod List (Modding.log) ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Mod Version: {Mod.VERSION}");
            sb.AppendLine();

            var path = Path.Combine(ModPaths.LogsDirectory, ModPaths.ModdingLogFile);
            try
            {
                // No File.Exists pre-check (TOCTOU); read a size-capped head via StreamReader (the mod
                // list is at the top, and a head cap bounds memory) instead of ReadAllLines.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var buffer = new char[MAX_LOG_TAIL_BYTES];
                int read = reader.Read(buffer, 0, buffer.Length);
                sb.Append(buffer, 0, read);
                if (read == buffer.Length)
                    sb.AppendLine().AppendLine("[Modding.log truncated]");
            }
            catch (FileNotFoundException)
            {
                sb.AppendLine("Modding.log not found (no active playset log yet).");
            }
            catch (Exception ex)
            {
                Log.Exception("Failed to read Modding.log for report", ex);
                sb.AppendLine($"Failed to read Modding.log: {ex.GetType().Name}: {SanitizeForReport(ex.Message, 512)}");
            }

            return Truncate(PiiRedactor.Redact(sb.ToString()), MAX_REPORT_CHARS);
        }

        /// <summary>
        /// Clear recorded errors.
        /// </summary>
        public static void ClearErrors()
        {
            lock (s_Lock)
            {
                s_RecentErrors.Clear();
                s_LastLoggedByKey.Clear();
            }
            Log.Info("Error log cleared");
        }

        /// <summary>
        /// Get count of recorded errors.
        /// </summary>
        public static int GetErrorCount()
        {
            lock (s_Lock)
            {
                return s_RecentErrors.Count;
            }
        }

        private static string GetReportFilePath()
        {
            return Path.Combine(ModPaths.LogsDirectory, ModPaths.BugReportFile);
        }

        private static string GetLogFilePath()
        {
            return Path.Combine(ModPaths.LogsDirectory, ModPaths.ModLogFile);
        }

        private static string GetPrevLogFilePath()
        {
            return Path.Combine(ModPaths.LogsDirectory, ModPaths.PrevModLogFile);
        }

        private static string[] ReadLastLines(string filePath, int lineCount)
        {
            var lines = ReadTailWindow(filePath, lineCount, out bool truncated);
            if (lines.Length > 0)
                return lines;

            // A live log can rotate/truncate between Length and Seek. Retry from
            // the beginning once so the report never ships a silently empty tail.
            var retry = ReadTailWindow(filePath, lineCount, out _);
            if (retry.Length > 0)
                return retry;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0)
                    return new[] { "[log tail empty]" };
            }

            return truncated
                ? new[] { "[log tail unavailable - file rotated/truncated during report]" }
                : new[] { "[log tail unavailable - no readable lines]" };
        }

        private static string[] ReadTailWindow(string filePath, int lineCount, out bool truncated)
        {
            truncated = false;
            var lines = new LinkedList<string>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long length = fs.Length;
                if (length > MAX_LOG_TAIL_BYTES)
                {
                    truncated = true;
                    long offset = Math.Max(0, length - MAX_LOG_TAIL_BYTES);
                    if (offset > fs.Length)
                        offset = 0;
                    fs.Seek(offset, SeekOrigin.Begin);
                }

                using var reader = new StreamReader(fs);
                if (truncated)
                    reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.AddLast(line);
                    if (lines.Count > lineCount)
                        lines.RemoveFirst();
                }
            }

            var result = new string[lines.Count];
            lines.CopyTo(result, 0);
            return result;
        }

        private static bool TryCopyToSystemClipboard(string report)
        {
            var guiUtilityType =
                Type.GetType("UnityEngine.GUIUtility, UnityEngine.IMGUIModule", throwOnError: false)
                ?? Type.GetType("UnityEngine.GUIUtility, UnityEngine.CoreModule", throwOnError: false);
            var copyBuffer = guiUtilityType?.GetProperty(
                "systemCopyBuffer",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (copyBuffer == null || !copyBuffer.CanWrite)
                return false;

            copyBuffer.SetValue(null, report);
            return true;
        }

        private static void EvictOldestDedupeKey()
        {
            lock (s_Lock)
            {
                if (s_LastLoggedByKey.Count == 0)
                    return;

                string? oldestKey = null;
                DateTime oldestTime = DateTime.MaxValue;
                foreach (var kvp in s_LastLoggedByKey)
                {
                    if (kvp.Value < oldestTime)
                    {
                        oldestTime = kvp.Value;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey != null)
                    s_LastLoggedByKey.Remove(oldestKey);
            }
        }

        private static string SanitizeForReport(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = Truncate(value, maxChars);
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                    continue;

                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? string.Empty;

            return value.Substring(0, maxChars);
        }
    }
}
