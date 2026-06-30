using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Disk persistence for telemetry events.
    /// Provides crash-safe JSONL logging and recovery.
    /// </summary>
    public sealed class TelemetryPersistence
    {
        private static readonly LogContext Log = new("TelemetryPersistence");
        private const int MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024; // 5 MB cap
        private const long MAX_RETRY_QUEUE_BYTES = 5 * 1024 * 1024; // 5 MB total across retry segments
        private const string IMMEDIATE_PREFIX = "telemetry_immediate_";
        private const string BATCH_PREFIX = "telemetry_batch_";
        private const string RETRY_PREFIX = "telemetry_retry_";
        // Dead queue filename from an earlier build (current pipeline writes
        // ModPaths.TelemetryPendingFile = "telemetry_pending.jsonl"). Files under this name are
        // never enumerated for recovery and never deleted → they orphan on disk forever. Purged
        // outright on startup; the queued events are too stale to resend.
        private const string LEGACY_QUEUE_FILE = "telemetry_queue.jsonl";
        private const string QUARANTINE_GLOB = "*.quarantine";
        private const string RECOVERING_SUFFIX = ".recovering";
        private const string RECOVERY_HEADER_PREFIX = "# civic-telemetry-recovery ";
        private readonly TelemetryConfig m_Config;
        private readonly object m_PendingLock = new();
        private readonly object m_RetryLock = new();
        private readonly object m_DebugLock = new();

        public TelemetryPersistence(TelemetryConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
            EnsureDirectoryExists();
            CleanStaleGarbage();
        }

        public string? AppendToDisk(TelemetryEvent evt)
        {
            try
            {
                var serializer = TelemetryJsonSerializer.Instance;
                if (serializer == null) return null;

                var json = serializer.SerializeEvent(evt);
                var path = CreateSegmentPath(IMMEDIATE_PREFIX);
                lock (m_PendingLock)
                {
                    WriteSegment(path, WithRecoveryHeader(json + "\n"));
                }

                return path;
            }
            catch (Exception ex)
            {
                Log.Error($" Disk write failed: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Write pre-serialized JSONL to a per-batch segment synchronously.
        /// The returned path is the only file a later acknowledgement may delete.
        /// </summary>
        public string? WriteBatchToDisk(string jsonl)
        {
            if (string.IsNullOrEmpty(jsonl)) return null;

            try
            {
                var path = CreateSegmentPath(BATCH_PREFIX);
                lock (m_PendingLock)
                {
                    WriteSegment(path, WithRecoveryHeader(jsonl));
                }

                return path;
            }
            catch (Exception ex)
            {
                Log.Error($" Disk write failed: {ex}");
                return null;
            }
        }

        public bool DeletePendingSegment(string? path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            try
            {
                lock (m_PendingLock)
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose.
                    File.Delete(path);
#pragma warning restore CIVIC147
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($" Pending segment delete failed: {ex}");
                return false;
            }
        }

        public List<TelemetryEvent> RecoverPendingEvents()
        {
            var result = new List<TelemetryEvent>();

            try
            {
                lock (m_PendingLock)
                {
                    foreach (var path in EnumeratePendingRecoveryFiles())
                    {
                        RecoverEventSegment(path, result);
                    }
                }

                if (result.Count > 0)
                {
                    Log.Info($" Recovered {result.Count} events from previous session");
                }
            }
            catch (Exception ex)
            {
                Log.Error($" Recovery failed: {ex}");
            }

            return result;
        }

        public bool HasRecoverablePendingEvents()
        {
            try
            {
                lock (m_PendingLock)
                {
                    return EnumeratePendingRecoveryFiles().Any(File.Exists);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($" Pending recovery evidence check failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Whole-log cleanup hook for manual/session resets. Batch acks delete only their own segment.
        /// </summary>
        public void ClearPendingLog()
        {
            DeletePendingSegment(GetPendingLogPath());
        }

        /// <summary>
        /// Erase ALL queued telemetry on disk — pending log, immediate/batch segments, and the
        /// retry queue (everything RecoverPendingEvents / RecoverRetryQueue would resend). Called
        /// on consent revoke (player turns Online off) so events collected while Online was on do
        /// not survive opt-out and silently upload on a later opt-in. NOT called on world
        /// destroy, where surviving the restart and resending is legitimate.
        /// </summary>
        public void ClearAllQueued()
        {
            int deleted;
            lock (m_PendingLock)
            {
                lock (m_RetryLock)
                {
                    deleted = DeleteFiles(EnumeratePendingRecoveryFiles().Concat(EnumerateRetryRecoveryFiles()));
                }
            }
            if (deleted > 0)
                Log.Info($" Cleared {deleted} queued telemetry segment(s) on consent revoke");
        }

        private static int DeleteFiles(IEnumerable<string> paths)
        {
            int n = 0;
            foreach (var path in paths)
            {
                try
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose.
                    File.Delete(path);
#pragma warning restore CIVIC147
                    n++;
                }
#pragma warning disable CIVIC052 // Best-effort: keep deleting if one fails.
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Queue clear skipped {Path.GetFileName(path)}: {ex.Message}");
                }
#pragma warning restore CIVIC052
            }
            return n;
        }

        /// <summary>
        /// Write pre-serialized JSON to debug log on a background thread.
        /// </summary>
        public void WriteDebugLogBackground(string json)
        {
            BackgroundTask.Run(() =>
            {
                lock (m_DebugLock)
                {
                    AtomicFileWriter.WriteAllText(GetDebugLogPath(), json);
                }
                Log.Info(" Saved telemetry to debug file");
            });
        }

        public bool AppendToRetryQueue(string json)
        {
            try
            {
                var retryLines = BuildRetryQueueLines(json);
                if (retryLines.Count == 0) return true;

                lock (m_RetryLock)
                {
                    // Each failed batch is its own atomic segment, mirroring the pending
                    // path. The old design read + rewrote one shared file on every append
                    // (O(total) per call → O(n^2) over a long outage); segments make each
                    // append O(batch) and bound growth by dropping oldest segments.
                    var path = CreateSegmentPath(RETRY_PREFIX);
                    WriteSegment(path, WithRecoveryHeader(string.Join("\n", retryLines) + "\n"));
                    EnforceRetrySegmentCap();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($" Retry queue append failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Bound the retry queue by total size, shedding the oldest segments first.
        /// Telemetry is best-effort: during a long server outage the newest events are
        /// the most useful, so old ones are dropped rather than letting the queue grow
        /// without limit (the previous rotate-to-.old kept everything recoverable).
        /// </summary>
        private void EnforceRetrySegmentCap()
        {
            var dir = m_Config.LogsDirectory;
            if (!Directory.Exists(dir)) return;

            var segments = new List<FileInfo>();
            long total = 0;
            foreach (var file in Directory.GetFiles(dir, RETRY_PREFIX + "*.jsonl"))
            {
                try
                {
                    var info = new FileInfo(file);
                    info.Refresh();
                    if (!info.Exists) continue;
                    segments.Add(info);
                    total += info.Length;
                }
#pragma warning disable CIVIC052 // Best-effort: an unreadable entry is skipped, not fatal.
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Retry cap scan skipped {Path.GetFileName(file)}: {ex.Message}");
                }
#pragma warning restore CIVIC052
            }

            if (total <= MAX_RETRY_QUEUE_BYTES) return;

            segments.Sort((a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
            int dropped = 0;
            foreach (var s in segments)
            {
                // Keep at least the newest segment even if it alone exceeds the cap.
                if (total <= MAX_RETRY_QUEUE_BYTES || segments.Count - dropped <= 1) break;
                try
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose.
                    File.Delete(s.FullName);
#pragma warning restore CIVIC147
                    total -= s.Length;
                    dropped++;
                }
#pragma warning disable CIVIC052 // Best-effort: keep shedding if one delete fails.
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Retry cap drop skipped {s.Name}: {ex.Message}");
                }
#pragma warning restore CIVIC052
            }

            if (dropped > 0)
                Log.Warn($" Retry queue over {MAX_RETRY_QUEUE_BYTES / (1024 * 1024)}MB cap, dropped {dropped} oldest segment(s)");
        }

        private static List<string> BuildRetryQueueLines(string json)
        {
            var lines = new List<string>();
            try
            {
                var events = TelemetryPayloadReader.ParseRetryRecord(json);
                var serializer = TelemetryJsonSerializer.Instance;
                foreach (var evt in events)
                {
                    lines.Add(serializer.SerializeEvent(evt));
                }
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Retry payload could not be split into event lines: {ex.Message}");
            }

            if (lines.Count == 0)
                lines.Add(json);

            return lines;
        }

        /// <summary>
        /// Recover failed payloads from the retry queue.
        /// Returns typed events only after their source segment was deleted.
        /// </summary>
        public List<TelemetryEvent> RecoverRetryQueue()
        {
            var lines = new List<string>();
            var result = new List<TelemetryEvent>();

            lock (m_RetryLock)
            {
                foreach (var path in EnumerateRetryRecoveryFiles())
                {
                    RecoverTextSegment(path, lines, "retry queue");
                }
            }
            foreach (var line in lines)
            {
                RecoverRetryLine(line, result);
            }

            if (result.Count > 0)
                Log.Info($" Recovered {result.Count} events from retry queue");

            return result;
        }

        private static void RecoverRetryLine(string line, List<TelemetryEvent> result)
        {
            try
            {
                result.Add(TelemetryPayloadReader.ParseEvent(line));
                return;
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Retry line is not a single event, trying legacy payload: {ex.Message}");
            }

            try
            {
                result.AddRange(TelemetryPayloadReader.ParseRetryRecord(line));
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Skipping corrupted retry entry: {ex.Message}");
            }
        }

        private void RecoverEventSegment(string path, List<TelemetryEvent> result)
        {
            var lines = new List<string>();
            RecoverTextSegment(path, lines, "pending telemetry");
            foreach (var line in lines)
            {
                try
                {
                    var evt = TelemetryPayloadReader.ParseEvent(line);
                    result.Add(evt);
                }
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Skipping corrupted line: {ex.Message}");
                }
            }
        }

        private void RecoverTextSegment(string path, List<string> result, string label)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var recoveringPath = path.EndsWith(RECOVERING_SUFFIX, StringComparison.Ordinal)
                ? path
                : path + RECOVERING_SUFFIX;

            try
            {
#pragma warning disable CIVIC147 // File IO is serialized by caller lock.
                if (!string.Equals(path, recoveringPath, StringComparison.Ordinal))
                {
                    try { File.Delete(recoveringPath); }
                    catch (FileNotFoundException ex)
                    {
                        if (Log.IsDebugEnabled) Log.Debug($" Recovery target already absent: {ex}");
                    }
                    File.Move(path, recoveringPath);
                }

                var recovered = new List<string>();
                bool quarantine = false;
                using (var reader = new StreamReader(recoveringPath))
                {
                    string? line;
                    bool firstContentLine = true;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (firstContentLine)
                        {
                            firstContentLine = false;
                            if (line.StartsWith(RECOVERY_HEADER_PREFIX, StringComparison.Ordinal))
                            {
                                if (!IsCurrentRecoveryHeader(line))
                                {
                                    Log.Warn($" {label} recovery format mismatch, quarantining {Path.GetFileName(path)}");
                                    quarantine = true;
                                    break;
                                }
                                continue;
                            }

                            Log.Warn($" {label} recovery cache missing version header, quarantining {Path.GetFileName(path)}");
                            quarantine = true;
                            break;
                        }

                        recovered.Add(line);
                    }
                }

                if (quarantine)
                {
                    QuarantineRecoveringSegment(recoveringPath);
                    return;
                }

                File.Delete(recoveringPath);
#pragma warning restore CIVIC147
                result.AddRange(recovered);
            }
            catch (Exception ex)
            {
                Log.Warn($" {label} recovery deferred for {Path.GetFileName(path)}: {ex}");
            }
        }

        private IEnumerable<string> EnumeratePendingRecoveryFiles()
        {
            var dir = m_Config.LogsDirectory;
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            var files = new List<string>();
            AddIfExists(files, GetPendingLogPath());
            AddIfExists(files, GetPendingLogPath() + ".old");
            AddIfExists(files, GetPendingLogPath() + RECOVERING_SUFFIX);
            files.AddRange(Directory.GetFiles(dir, ModPaths.TelemetryPendingFile + "*.old"));
            files.AddRange(Directory.GetFiles(dir, IMMEDIATE_PREFIX + "*.jsonl"));
            files.AddRange(Directory.GetFiles(dir, IMMEDIATE_PREFIX + "*.old"));
            files.AddRange(Directory.GetFiles(dir, IMMEDIATE_PREFIX + "*" + RECOVERING_SUFFIX));
            files.AddRange(Directory.GetFiles(dir, BATCH_PREFIX + "*.jsonl"));
            files.AddRange(Directory.GetFiles(dir, BATCH_PREFIX + "*.old"));
            files.AddRange(Directory.GetFiles(dir, BATCH_PREFIX + "*" + RECOVERING_SUFFIX));
            return files.Distinct(StringComparer.Ordinal).OrderBy(File.GetCreationTimeUtc).ToArray();
        }

        private IEnumerable<string> EnumerateRetryRecoveryFiles()
        {
            var dir = m_Config.LogsDirectory;
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            var files = new List<string>();
            files.AddRange(Directory.GetFiles(dir, RETRY_PREFIX + "*.jsonl"));
            files.AddRange(Directory.GetFiles(dir, RETRY_PREFIX + "*" + RECOVERING_SUFFIX));
            return files.Distinct(StringComparer.Ordinal).OrderBy(File.GetCreationTimeUtc).ToArray();
        }

        private static void AddIfExists(List<string> files, string path)
        {
            if (File.Exists(path)) files.Add(path);
        }

        private string CreateSegmentPath(string prefix)
        {
            return Path.Combine(m_Config.LogsDirectory, $"{prefix}{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}.jsonl");
        }

        private static void WriteSegment(string path, string content)
        {
            if (content.Length > MAX_FILE_SIZE_BYTES)
            {
                Log.Warn($" Telemetry segment exceeds size cap: {Path.GetFileName(path)}");
            }

#pragma warning disable CIVIC147 // File IO is the lock's purpose.
            AtomicFileWriter.WriteAllText(path, content);
#pragma warning restore CIVIC147
        }

        private static string WithRecoveryHeader(string content)
        {
            return RecoveryHeaderLine() + "\n" + content;
        }

        private static string RecoveryHeaderLine()
        {
            return RECOVERY_HEADER_PREFIX
                + "format=" + TelemetryContract.RecoveryFormatVersion
                + ";contract=" + TelemetryContract.CurrentVersion;
        }

        // Quarantine is for a genuine on-disk STRUCTURE incompatibility — the recovery format
        // (RecoveryFormatVersion), not the telemetry contract version. Both travel in the header
        // (format=…;contract=…) but mean different things: the format governs how the JSONL is
        // laid out, the contract governs the event schema, which the server accepts for anything
        // at or above its minSupportedVersion floor. Gating on the full header line meant every
        // additive contract bump (1.2→1.8 was six of them) quarantined valid, server-acceptable
        // telemetry merely queued under an earlier contract — it then accumulated on disk forever
        // and never reached the server. Match the format token alone so queued events survive
        // contract bumps and actually get delivered; the contract field stays in the header for
        // forensics but no longer gates recovery.
        private static bool IsCurrentRecoveryHeader(string line)
        {
            var format = ExtractHeaderToken(line, "format=");
            return string.Equals(format, TelemetryContract.RecoveryFormatVersion, StringComparison.Ordinal);
        }

        private static string? ExtractHeaderToken(string line, string key)
        {
            var start = line.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;

            start += key.Length;
            var end = line.IndexOf(';', start);
            if (end < 0) end = line.Length;
            return line.Substring(start, end - start).Trim();
        }

        private static void QuarantineRecoveringSegment(string recoveringPath)
        {
            try
            {
                if (!File.Exists(recoveringPath))
                    return;

                string quarantinePath = recoveringPath + "." + DateTime.UtcNow.Ticks + ".quarantine";
                File.Move(recoveringPath, quarantinePath);
            }
            catch (Exception ex)
            {
                Log.Warn($" Recovery quarantine failed for {Path.GetFileName(recoveringPath)}: {ex}");
            }
        }

        private string GetPendingLogPath()
        {
            return Path.Combine(m_Config.LogsDirectory, ModPaths.TelemetryPendingFile);
        }

        private string GetDebugLogPath()
        {
#pragma warning disable CIVIC016 // Debug-only file, not shipped to users.
            return Path.Combine(m_Config.LogsDirectory, "telemetry_debug.json");
#pragma warning restore CIVIC016
        }

        private const int STALE_OLD_FILE_DAYS = 7;

        /// <summary>
        /// Startup purge of telemetry dead-ends that the pipeline never sends and never deletes,
        /// so they cannot accumulate without bound on a player's disk:
        /// <list type="bullet">
        /// <item>rotated <c>.old</c> segments and quarantined segments — aged out after
        /// <see cref="STALE_OLD_FILE_DAYS"/> days (kept briefly for forensics, never resent);</item>
        /// <item>the legacy <see cref="LEGACY_QUEUE_FILE"/> orphan — deleted outright (the current
        /// pipeline neither writes nor reads it, and the queued events are too stale to resend).</item>
        /// </list>
        /// </summary>
        private void CleanStaleGarbage()
        {
            try
            {
                var dir = m_Config.LogsDirectory;
                if (!Directory.Exists(dir)) return;

                DeleteAgedFiles(dir, "*.old");
                DeleteAgedFiles(dir, QUARANTINE_GLOB);
                DeleteLegacyOrphans(dir);
            }
#pragma warning disable CIVIC052 // Best-effort cleanup.
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Stale garbage cleanup scan skipped: {ex}");
            }
#pragma warning restore CIVIC052
        }

        private static void DeleteAgedFiles(string dir, string pattern)
        {
            foreach (var file in Directory.GetFiles(dir, pattern))
            {
                try
                {
                    var info = new FileInfo(file);
                    info.Refresh();
                    if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc).TotalDays > STALE_OLD_FILE_DAYS)
                    {
                        File.Delete(file);
                        if (Log.IsDebugEnabled) Log.Debug($" Deleted stale file: {Path.GetFileName(file)}");
                    }
                }
#pragma warning disable CIVIC052 // Best-effort cleanup.
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Stale cleanup skipped for {Path.GetFileName(file)}: {ex}");
                }
#pragma warning restore CIVIC052
            }
        }

        private static void DeleteLegacyOrphans(string dir)
        {
            // Match the dead base name plus any rotation/recovery suffix it may have picked up.
            int deleted = 0;
            foreach (var file in Directory.GetFiles(dir, LEGACY_QUEUE_FILE + "*"))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
#pragma warning disable CIVIC052 // Best-effort cleanup.
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($" Legacy orphan delete skipped for {Path.GetFileName(file)}: {ex}");
                }
#pragma warning restore CIVIC052
            }

            if (deleted > 0)
                Log.Info($" Removed {deleted} orphaned legacy telemetry file(s)");
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(m_Config.LogsDirectory))
                {
                    Directory.CreateDirectory(m_Config.LogsDirectory);
                }
            }
            catch (Exception ex)
            {
                Log.Error($" Failed to create logs directory: {ex}");
            }
        }
    }
}
