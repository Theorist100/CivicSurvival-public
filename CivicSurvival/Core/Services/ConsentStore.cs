using System;
using System.IO;
using System.Text;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Global, save-independent persistence for boolean user consent flags.
    ///
    /// Each consent key is backed by its own small text file in
    /// <see cref="ModPaths.ModDataDirectory"/> (next to the native crash breadcrumb),
    /// so a flag is readable at mod init — before any city save is deserialized. This
    /// is what makes a consent decision survive across saves and restarts instead of
    /// being tied to a single city save.
    ///
    /// Used for:
    /// <list type="bullet">
    /// <item><see cref="ConsentKey.Telemetry"/> — anonymous diagnostics opt-in. Read at
    /// init by <c>TelemetryConfig.Load</c> so crash detection at <c>OnCreate</c> sees
    /// the real consent state.</item>
    /// <item><see cref="ConsentKey.OnlineConnection"/> — Global Grid (online news/stats)
    /// opt-in. A global user preference, not a per-city setting.</item>
    /// </list>
    /// </summary>
    public static class ConsentStore
    {
        private static readonly LogContext Log = new("ConsentStore");

        // Flag is "true"/"false" — anything larger is corrupt; bound the read.
        private const long MaxFileBytes = 64;

        private static string PathFor(ConsentKey key)
            => Path.Combine(ModPaths.ModDataDirectory, FileNameFor(key));

        private static string FileNameFor(ConsentKey key) => key switch
        {
            // Telemetry keeps its original file name so existing players' opt-in is not lost.
            ConsentKey.Telemetry => ModPaths.TelemetryOptInFile,
            ConsentKey.OnlineConnection => ModPaths.OnlineConnectionFile,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown consent key"),
        };

        /// <summary>
        /// True when the global file for <paramref name="key"/> exists. Used to
        /// distinguish "explicit false consent" from "no consent recorded yet" (e.g. a
        /// save made before this store existed), which lets a one-time migration carry
        /// forward in-save consent instead of silently dropping it on load.
        /// </summary>
        public static bool Exists(ConsentKey key)
        {
            try { return new FileInfo(PathFor(key)).Exists; }
            catch (Exception ex)
            {
                Log.Warn($" Consent existence check failed ({key}): {ex}");
                return false;
            }
        }

        /// <summary>
        /// Reads the persisted consent flag. Returns false when absent or unreadable
        /// (safe default: no consent unless explicitly granted).
        /// </summary>
        public static bool Read(ConsentKey key)
        {
            try
            {
                var info = new FileInfo(PathFor(key));
                if (!info.Exists)
                    return false;
                if (info.Length > MaxFileBytes)
                    return false;

                using var reader = new StreamReader(info.FullName, Encoding.UTF8);
                var value = (reader.ReadLine() ?? string.Empty).Trim();
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || value == "1";
            }
            catch (Exception ex)
            {
                Log.Warn($" Consent read failed ({key}): {ex}");
                return false;
            }
        }

        /// <summary>
        /// Persists the consent flag globally so the next mod init can read it before
        /// the save loads. Best-effort; logs on failure but never throws.
        /// </summary>
        public static void Write(ConsentKey key, bool enabled)
        {
            try
            {
                Directory.CreateDirectory(ModPaths.ModDataDirectory);
                // Direct in-place write (1 syscall) instead of atomic temp+replace (3-4): tiny "true"/
                // "false" flag rewritten whole, on the UI thread into the OneDrive/AV ANR zone. A torn
                // flag is rejected by the reader and falls back to default-off (best-effort consent).
                AtomicFileWriter.WriteAllTextDirect(PathFor(key), enabled ? "true" : "false", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warn($" Consent write failed ({key}): {ex}");
            }
        }
    }

    /// <summary>
    /// Identifies a global, save-independent consent flag. Each key maps to its own
    /// file in <see cref="ModPaths.ModDataDirectory"/>.
    /// </summary>
    public enum ConsentKey
    {
        /// <summary>Anonymous diagnostics opt-in (file: telemetry_optin.txt).</summary>
        Telemetry = 0,

        /// <summary>Global Grid online connection opt-in (file: online_connection.txt).</summary>
        OnlineConnection = 1,
    }
}
