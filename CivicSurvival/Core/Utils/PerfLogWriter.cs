using System;
using System.IO;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Owns the PERF.log file writer. Thread-safe write with auto-recovery.
    /// </summary>
    internal static class PerfLogWriter
    {
        private static readonly LogContext Log = new("Profiler");
        private static StreamWriter s_Writer = null!;
        private static readonly object s_WriterLock = new();
        private static string s_LogPath = null!;
        private static int s_RecoveryFailures;
        private static volatile bool s_RecoveryBannerWritten;
        private const int MAX_RECOVERY_FAILURES = 3;

        public static void Initialize(string logDirectory, double thresholdMs, float reportIntervalSec)
        {
            string logPath = Path.Combine(logDirectory, ModPaths.PerfLogFile);
            try
            {
                lock (s_WriterLock)
                {
                    s_Writer?.Dispose();
                    s_Writer = null!;
                    s_LogPath = logPath;
                    s_RecoveryFailures = 0;
                    s_RecoveryBannerWritten = false;

                    // Overwrite on each session
                    s_Writer = new StreamWriter(s_LogPath, append: false) { AutoFlush = true };
                    s_Writer.WriteLine($"=== CivicSurvival Performance Log === {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    s_Writer.WriteLine($"Threshold: {thresholdMs}ms | Report interval: {reportIntervalSec}s");
                    s_Writer.WriteLine();
                    s_Writer.WriteLine("NOTE on per-system numbers below:");
                    s_Writer.WriteLine("  These reflect main-thread cost only — scheduling overhead, sync points,");
                    s_Writer.WriteLine("  structural changes, ECB playback, and any work done synchronously on the");
                    s_Writer.WriteLine("  main thread. Job execution on worker threads is NOT captured here, because");
                    s_Writer.WriteLine("  Burst-compiled jobs run outside SystemBase.Update(). Sync overhead is");
                    s_Writer.WriteLine("  measured exactly via the SYNC POINT COST table (Full:X.OnUpdate minus");
                    s_Writer.WriteLine("  X.OnUpdate). For per-job profiling attach Unity Profiler to the running game.");
                    s_Writer.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to create PERF.log: {ex}");
                lock (s_WriterLock)
                {
                    s_Writer = null!;
                    s_LogPath = null!;
                    s_RecoveryFailures = MAX_RECOVERY_FAILURES;
                    s_RecoveryBannerWritten = false;
                }
            }
        }

        public static void Shutdown()
        {
#pragma warning disable CIVIC052 // Cleanup: can't log during dispose
            try
            {
                lock (s_WriterLock)
                {
                    s_Writer?.Dispose();
                    s_Writer = null!;
                    s_LogPath = null!;
                    s_RecoveryFailures = 0;
                    s_RecoveryBannerWritten = false;
                }
            }
            catch { /* ignore */ }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// Write a timestamped marker line (e.g. toggle events, A/B phase changes).
        /// </summary>
        public static void WriteMarker(string message)
        {
            Write($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public static void Write(string message)
        {
            try
            {
                bool writerFallback = false;
                lock (s_WriterLock)
                {
                    if (s_Writer != null)
                    {
                        s_Writer.WriteLine(message);
                    }
                    else
                    {
                        writerFallback = true;
                    }
                }
                // Fallback to main log if PERF.log not available
                if (writerFallback)
                    Log.Info($"{message}");
            }
#pragma warning disable CIVIC052 // Perf log: recovery on write failure
            catch (Exception ex)
            {
                // Writer broken — try to recreate once
                try
                {
                    lock (s_WriterLock)
                    {
                        s_Writer?.Dispose();
                        s_Writer = null!;

                        if (s_LogPath != null && s_RecoveryFailures < MAX_RECOVERY_FAILURES)
                        {
                            s_Writer = new StreamWriter(s_LogPath, append: true) { AutoFlush = true };
                            if (!s_RecoveryBannerWritten)
                            {
                                s_Writer.WriteLine($"\n=== Writer recovered at {DateTime.Now:HH:mm:ss} (error: {ex.GetType().Name}) ===\n");
                                s_RecoveryBannerWritten = true;
                            }
                            s_Writer.WriteLine(message);
                            s_RecoveryFailures = 0;
                        }
                        else if (s_RecoveryFailures >= MAX_RECOVERY_FAILURES)
                        {
                            s_LogPath = null!;
                        }
                    }
                }
                catch
                {
                    lock (s_WriterLock)
                    {
                        s_Writer = null!;
                        s_RecoveryFailures++;
                        if (s_RecoveryFailures >= MAX_RECOVERY_FAILURES)
                            s_LogPath = null!;
                    }
                }
            }
#pragma warning restore CIVIC052
        }
    }
}
