using System;
using System.Globalization;
using System.IO;
using System.Text;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Unified native crash breadcrumb pipeline: the same marker is written to
    /// Backtrace/crashpad and to a tiny local recovery file for next-launch telemetry.
    /// </summary>
    public static class NativeCrashBreadcrumb
    {
        private const int MaxMarkerChars = 128;
        private const int MaxMarkerFileBytes = 2048;
        private static readonly LogContext Log = new("NativeCrashBreadcrumb");
        private static readonly object s_Lock = new();

        private static volatile bool s_Enabled;
        // Markers currently in flight. Concurrent risk regions (TMS movement, AA
        // targeting, debris) re-Mark every frame; the set dedups those so only a
        // change in the active membership touches disk. s_LastMarker is the marker
        // persisted to the file (most-recent entrant still in flight).
        private static readonly System.Collections.Generic.HashSet<string> s_Active = new(StringComparer.Ordinal);
        private static string s_LastMarker = string.Empty;

        // Ring of the last N markers that became the persisted current marker, with the time each
        // did. Shows the SEQUENCE of phases in the final window (the single sticky marker only shows
        // the most-recent-still-in-flight, so a vanilla crash inherits a coarse resident marker). Read
        // on recovery to attribute the crash. Appended ONLY from WriteMarkerLocked — i.e. only on an
        // active-set membership change (new Mark entrant or ClearIfCurrent rewrite), never per-tick.
        private const int RingCapacity = 8;
        private static readonly (string Marker, DateTime Utc)[] s_Ring = new (string, DateTime)[RingCapacity];
        private static int s_RingHead;   // next write slot
        private static int s_RingCount;  // filled slots (≤ RingCapacity)

        public static void SetEnabled(bool enabled)
        {
            lock (s_Lock)
            {
                s_Enabled = enabled;
                if (!enabled)
                    ClearLocked();
            }
        }

        public static void Mark(string marker)
        {
            if (!IsValidMarker(marker))
            {
                Log.Warn($" Ignoring unknown native crash marker: {marker}");
                return;
            }

            lock (s_Lock)
            {
                if (!s_Enabled) return;
                // PERF-LOCK: active-set dedup — a marker already in flight needs no rewrite.
                // Keeps the per-frame hot path write-free; only membership changes touch disk.
                if (!s_Active.Add(marker)) return;

                BacktraceMarkers.Phase(marker);
                WriteMarkerLocked(marker);
                s_LastMarker = marker;
            }
        }

        public static void ClearIfCurrent(string marker)
        {
            if (!IsValidMarker(marker))
                return;

            lock (s_Lock)
            {
                if (!s_Active.Remove(marker))
                    return;

                if (s_Active.Count == 0)
                {
                    ClearLocked();
                    return;
                }

                // Another region is still in flight; rewrite only if the departing
                // marker was the one persisted to disk.
                if (string.Equals(s_LastMarker, marker, StringComparison.Ordinal))
                {
                    var next = FirstActiveLocked();
                    BacktraceMarkers.Phase(next);
                    WriteMarkerLocked(next);
                    s_LastMarker = next;
                }
            }
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                ClearLocked();
            }
        }

        /// <summary>
        /// Snapshot of the recent-marker ring (oldest→newest, "marker@utc;…") for callers outside the
        /// breadcrumb writer — used to mirror the phase sequence into the persisted context file so it
        /// survives an UNMARKED unclean shutdown (the marker file is absent then, but the context file
        /// is not). Read at the context-write cadence (every telemetry pulse), never per-frame.
        /// </summary>
        public static string GetRecentMarkers()
        {
            lock (s_Lock)
            {
                return BuildRecentMarkersLocked();
            }
        }

        [CallerHoldsLock(nameof(s_Lock))]
        private static string FirstActiveLocked()
        {
            using var enumerator = s_Active.GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : string.Empty;
        }

        public static bool TryConsume(out NativeCrashBreadcrumbRecord record)
        {
            record = NativeCrashBreadcrumbRecord.Empty;
            string value;

            try
            {
                var info = new FileInfo(MarkerPath);
                info.Refresh();
                if (!info.Exists)
                {
                    lock (s_Lock)
                    {
                        s_Active.Clear();
                        s_LastMarker = string.Empty;
                    }
                    return false;
                }

                if (info.Length > MaxMarkerFileBytes)
                {
                    Clear();
                    Log.Warn($" Discarded oversized native crash marker file: {info.Length} bytes");
                    return false;
                }

                using var reader = new StreamReader(info.FullName, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                value = reader.ReadToEnd().Trim();
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash marker recovery failed: {ex}");
                return false;
            }

            Clear();

            if (!TryParseRecord(value, out record))
            {
                Log.Warn($" Discarded invalid native crash marker: {value}");
                return false;
            }

            return true;
        }

        private static bool IsValidMarker(string marker)
            => !string.IsNullOrWhiteSpace(marker)
            && marker.Length <= MaxMarkerChars
            && NativeCrashMarkers.IsKnown(marker);

        [CallerHoldsLock(nameof(s_Lock))]
        private static void WriteMarkerLocked(string marker)
        {
            // PERF-LOCK: ring append + disk write happen ONLY here, and WriteMarkerLocked is reached
            // ONLY on an active-set membership change (a new Mark entrant past the s_Active.Add dedup,
            // or a ClearIfCurrent rewrite) — never on the per-tick re-Mark path. Appending per-tick
            // would break the write-free dedup invariant (Axiom 15).
            AppendRingLocked(marker);
            try
            {
                Directory.CreateDirectory(ModPaths.ModDataDirectory);
                // Direct in-place write (1 syscall) instead of atomic temp+replace (3-4): this runs on
                // the sim/UI thread on every marker membership change, and the LocalLow target is the
                // OneDrive/AV-scanned ANR zone. A torn breadcrumb is harmless — TryParseRecord discards
                // it on recovery — but it MUST hit disk synchronously before a crash, so the write
                // stays on this thread (a deferred write could be lost on an instant crash).
                AtomicFileWriter.WriteAllTextDirect(MarkerPath, BuildRecordJson(marker, CrashContextProvider.Current));
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash marker write failed: {ex}");
            }
        }

        [CallerHoldsLock(nameof(s_Lock))]
        private static void AppendRingLocked(string marker)
        {
#pragma warning disable CIVIC003 // Diagnostic wall-clock timestamp for crash-phase ordering; membership-change cadence, not per-tick.
            var now = DateTime.UtcNow;
#pragma warning restore CIVIC003
            s_Ring[s_RingHead] = (marker, now);
            s_RingHead = (s_RingHead + 1) % RingCapacity;
            if (s_RingCount < RingCapacity) s_RingCount++;
        }

        [CallerHoldsLock(nameof(s_Lock))]
        private static string BuildRecentMarkersLocked()
        {
            if (s_RingCount == 0) return string.Empty;
            // Oldest → newest. When full, the oldest is at s_RingHead; otherwise the buffer starts at 0.
            int start = s_RingCount < RingCapacity ? 0 : s_RingHead;
            var sb = new StringBuilder(s_RingCount * 64);
            for (int i = 0; i < s_RingCount; i++)
            {
                int idx = (start + i) % RingCapacity;
                var entry = s_Ring[idx];
                if (sb.Length > 0) sb.Append(';');
                sb.Append(entry.Marker).Append('@').Append(entry.Utc.ToString("O", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        [CallerHoldsLock(nameof(s_Lock))]
        private static void ClearLocked()
        {
            s_Active.Clear();
            s_LastMarker = string.Empty;
            s_RingHead = 0;
            s_RingCount = 0;

            try
            {
                File.Delete(MarkerPath);
            }
            catch (DirectoryNotFoundException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Native crash marker directory already absent: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Native crash marker already absent: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Warn($" Native crash marker clear failed: {ex}");
            }
        }

        private static string MarkerPath
            => Path.Combine(ModPaths.ModDataDirectory, ModPaths.NativeCrashBreadcrumbFile);

        [CallerHoldsLock(nameof(s_Lock))]
        private static string BuildRecordJson(string marker, CrashContextSnapshot snapshot)
        {
#pragma warning disable CIVIC003 // Diagnostic wall-clock timestamp for cross-process crash correlation.
            var capturedUtc = DateTime.UtcNow;
#pragma warning restore CIVIC003

            // Identity of the crashing run, captured at write time. The relaunch session that
            // recovers this file has a different session id and possibly a newer mod version,
            // so sessions.mod_version is the wrong anchor — these fields carry the truth.
            var identity = CrashBreadcrumbIdentity.Current;

            var builder = JsonBuilder.Object()
                .Add("marker", marker)
                .Add("capturedUtc", capturedUtc.ToString("O", CultureInfo.InvariantCulture))
                .Add("modVersion", identity.ModVersion)
                .Add("sessionId", identity.SessionId)
                // Phase sequence (ring) + native-AV invariant scalars — attribution without a dump.
                .Add("recentMarkers", BuildRecentMarkersLocked())
                .Add("spatialHashCount", CrashScalars.SpatialHashCount.ToString(CultureInfo.InvariantCulture))
                .Add("spatialHashCapacity", CrashScalars.SpatialHashCapacity.ToString(CultureInfo.InvariantCulture))
                .Add("buildingCacheLength", CrashScalars.BuildingCacheLength.ToString(CultureInfo.InvariantCulture))
                .Add("lastJob", CrashScalars.LastJob)
                .Add("lastFilePath", CrashScalars.LastFilePath);

            if (!snapshot.IsEmpty)
            {
                builder
                    .Add("gameDay", snapshot.GameDay.ToString(CultureInfo.InvariantCulture))
                    .Add("act", snapshot.Act)
                    .Add("activeThreats", snapshot.ActiveThreats.ToString(CultureInfo.InvariantCulture))
                    .Add("waveNumber", snapshot.WaveNumber.ToString(CultureInfo.InvariantCulture))
                    .Add("population", snapshot.Population.ToString(CultureInfo.InvariantCulture))
                    .Add("managedMemoryMb", snapshot.ManagedMemoryMb.ToString(CultureInfo.InvariantCulture))
                    .Add("nativeMemoryMb", snapshot.NativeMemoryMb.ToString(CultureInfo.InvariantCulture))
                    .Add("contextCapturedUtc", snapshot.CapturedUtc.ToString("O", CultureInfo.InvariantCulture));
            }

            return builder.Build();
        }

        private static bool TryParseRecord(string value, out NativeCrashBreadcrumbRecord record)
        {
            record = NativeCrashBreadcrumbRecord.Empty;

            try
            {
                var values = JsonStream.ParseFlatStringDict(value);
                if (!values.TryGetValue("marker", out var marker) || !IsValidMarker(marker))
                    return false;

                var snapshot = new CrashContextSnapshot(
                    ReadInt(values, "gameDay"),
                    ReadString(values, "act"),
                    ReadInt(values, "activeThreats"),
                    ReadInt(values, "waveNumber"),
                    ReadInt(values, "population"),
                    ReadInt(values, "managedMemoryMb"),
                    ReadInt(values, "nativeMemoryMb"),
                    ReadUtc(values, "contextCapturedUtc"));

                record = new NativeCrashBreadcrumbRecord(
                    marker,
                    snapshot,
                    ReadUtc(values, "capturedUtc"),
                    ReadString(values, "modVersion"),
                    ReadString(values, "sessionId"),
                    ReadString(values, "recentMarkers"),
                    ReadInt(values, "spatialHashCount"),
                    ReadInt(values, "spatialHashCapacity"),
                    ReadInt(values, "buildingCacheLength"),
                    ReadString(values, "lastJob"),
                    ReadString(values, "lastFilePath"));
                return true;
            }
            catch (FormatException ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Native crash marker JSON parse failed: {ex.Message}");
                return false;
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
    }

    public readonly struct NativeCrashBreadcrumbRecord
    {
        public readonly string Marker;
        public readonly CrashContextSnapshot Context;
        public readonly DateTime CapturedUtc;
        // Identity of the run that actually crashed (embedded at write time). Empty for legacy
        // breadcrumb files written before identity capture, or when the unclean shutdown left
        // no marker file (then taken from the persisted context file's identity instead).
        public readonly string CrashModVersion;
        public readonly string CrashedSessionId;
        // Phase sequence in the final window (oldest→newest, "marker@utc;…") + native-AV invariant
        // scalars. SpatialHashCount == SpatialHashCapacity at crash ⇒ the false-exhaustion overrun
        // class (fix a57f0e912). Empty/0 for legacy files written before these fields existed.
        public readonly string RecentMarkers;
        public readonly int SpatialHashCount;
        public readonly int SpatialHashCapacity;
        public readonly int BuildingCacheLength;
        public readonly string LastJob;
        // Last file path our code was about to open for a sync write (CrashScalars.LastFilePath at write
        // time). Names the file when the main thread froze in CreateFileW — sync-IO ANR attribution.
        public readonly string LastFilePath;

        public NativeCrashBreadcrumbRecord(
            string marker,
            CrashContextSnapshot context,
            DateTime capturedUtc,
            string crashModVersion,
            string crashedSessionId,
            string recentMarkers,
            int spatialHashCount,
            int spatialHashCapacity,
            int buildingCacheLength,
            string lastJob,
            string lastFilePath)
        {
            Marker = marker ?? string.Empty;
            Context = context;
            CapturedUtc = capturedUtc == default ? DateTime.MinValue : capturedUtc.ToUniversalTime();
            CrashModVersion = crashModVersion ?? string.Empty;
            CrashedSessionId = crashedSessionId ?? string.Empty;
            RecentMarkers = recentMarkers ?? string.Empty;
            SpatialHashCount = spatialHashCount;
            SpatialHashCapacity = spatialHashCapacity;
            BuildingCacheLength = buildingCacheLength;
            LastJob = lastJob ?? string.Empty;
            LastFilePath = lastFilePath ?? string.Empty;
        }

        public static NativeCrashBreadcrumbRecord Empty => default;
    }
}
