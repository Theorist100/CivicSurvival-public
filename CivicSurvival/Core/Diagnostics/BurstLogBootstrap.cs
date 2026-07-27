using System.IO;
#if ENABLE_BURST
using Unity.Logging;
using Unity.Logging.Internal.Debug;
using Unity.Logging.Sinks;
#endif

namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Bootstraps a Unity.Logging <see cref="Logger"/> backed by a file sink. Unlike the Colossal
    /// <c>Mod.Log</c> (managed-only) this logger is Burst-compatible, so it can be called from inside
    /// Burst-compiled jobs to name the job/state right before a native AV. Writes a dedicated file so
    /// it never interleaves with the main mod log.
    ///
    /// Uses ONLY the file sink — the editor-console sink (UnityEditorConsole) pulls UnityEditor types
    /// that do not exist in the shipped game; touching it would TypeLoad at runtime.
    /// </summary>
    internal static class BurstLogBootstrap
    {
        private static bool s_Initialized;

#if ENABLE_BURST
        private const long MaxLogFileSizeBytes = 512L * 1024L * 1024L;

        // Reuses LogContext.IsDebugEnabled (reads Mod.Log.isDebugEnabled, try/catch-guarded).
        private static readonly CivicSurvival.Core.Utils.LogContext s_Gate = new("BurstDiag");

        /// <summary>
        /// Gate for the [BURSTMARK] schedule markers. Only ON in Debug log level so production
        /// (Info) stays clean and the hot-path Schedule sites pay nothing. Check this BEFORE the
        /// interpolated message so the string is never built when disabled.
        /// </summary>
        public static bool MarkersEnabled => s_Gate.IsDebugEnabled;
#else
        /// <summary>
        /// When the project Burst toggle is off, make marker call sites compile away.
        /// This prevents Unity.Logging sourcegen from leaving WriteBursted* methods in
        /// the mod assembly and keeps the diagnostic build free of mod Burst AOT.
        /// </summary>
        public static bool MarkersEnabled => false;
#endif

        /// <summary>Path of the diagnostic log file (under the game's Logs folder).</summary>
        public static string LogFilePath { get; private set; } = string.Empty;

        public static void Initialize()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;

            string logsDir = CivicSurvival.Core.Config.ModPaths.LogsDirectory;
            Directory.CreateDirectory(logsDir);
            LogFilePath = Path.Combine(logsDir, CivicSurvival.Core.Config.ModPaths.BurstDiagFile);

#if ENABLE_BURST
            // FullSync: write each message straight to the file on the calling thread. Async/batched
            // mode buffers in a DispatchQueue and loses the tail on a native AV — useless for crash
            // diagnostics (and left the file empty on first try). Sync trades throughput for durability,
            // which is exactly the right trade for naming the job right before a crash.
            SelfLog.SetMode(SelfLog.Mode.EnabledInUnityEngineDebugLogError);

            Log.Logger = new LoggerConfig()
                .MinimumLevel.Debug()
                .SyncMode.FullSync()
                .CaptureStacktrace(false)
                .WriteTo.File(
                    LogFilePath,
                    maxFileSizeBytes: MaxLogFileSizeBytes,
                    captureStackTrace: false,
                    minLevel: LogLevel.Verbose)
                .CreateLogger(LogMemoryManagerParameters.HeavyLoad);

            Log.Info("BurstDiag logger initialized");
#endif
        }

        public static void Info(string message)
        {
#if ENABLE_BURST
            Log.Info(message);
#endif
        }
    }
}
