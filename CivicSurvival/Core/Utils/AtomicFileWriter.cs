using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using CivicSurvival.Core.Diagnostics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Atomic file write operations using temp-file + rename pattern.
    /// Prevents data corruption on crash/power loss during write.
    /// NTFS guarantees atomic rename within same volume.
    /// </summary>
    public static class AtomicFileWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private const int ReplaceAttempts = 3;

        /// <summary>
        /// Atomically write text to file. Writes to .tmp first, then renames.
        /// </summary>
        public static void WriteAllText(string path, string content, Encoding encoding = null!)
        {
            // Record the target before the blocking CreateFile so an ANR frozen here names the file.
            CrashScalars.LastFilePath = path;
            var tmp = CreateTempPath(path);
            try
            {
                File.WriteAllText(tmp, content, encoding ?? Utf8NoBom);
                ReplaceWithTemp(tmp, path);
            }
            finally
            {
                DeleteTempIfPresent(tmp);
            }
        }

        /// <summary>
        /// Write text to <paramref name="path"/> in place with a single <c>CreateFile</c> syscall —
        /// no GUID temp file, no Replace/Move, no Delete, no retry loop.
        ///
        /// The atomic temp+rename path costs 3-4 separate blocking syscalls per call (create a fresh
        /// temp, write, replace, delete). On a slow / OneDrive-synced / AV-scanned volume — and the
        /// LocalLow ModData/Logs targets are exactly that OneDrive-redirect zone — each CreateFile can
        /// stall ≥15 s and trip the game's main-thread ANR watchdog. Writing in place to a known path
        /// is one CreateFile the AV/OneDrive already has cached.
        ///
        /// Use ONLY for tiny diagnostic/flag files that are rewritten whole and where atomicity buys
        /// nothing worth an ANR: the sole guarantee lost is "no half-written file if the process dies
        /// mid-write", and a torn diagnostic record is simply discarded by its parser (e.g.
        /// <c>TryParseRecord</c> returns false, <c>TryReadPersisted</c> ignores malformed JSON). Durable
        /// data where a torn write matters (telemetry segments, arena save) MUST keep <see cref="WriteAllText"/>.
        /// </summary>
        public static void WriteAllTextDirect(string path, string content, Encoding encoding = null!)
        {
            // Record the target before the blocking CreateFile so an ANR frozen here names the file.
            CrashScalars.LastFilePath = path;
            File.WriteAllText(path, content, encoding ?? Utf8NoBom);
        }

        /// <summary>
        /// Atomically write lines to file. Writes to .tmp first, then renames.
        /// </summary>
        public static void WriteAllLines(string path, string[] lines)
        {
            // Record the target before the blocking CreateFile so an ANR frozen here names the file.
            CrashScalars.LastFilePath = path;
            var tmp = CreateTempPath(path);
            try
            {
                File.WriteAllLines(tmp, lines, Utf8NoBom);
                ReplaceWithTemp(tmp, path);
            }
            finally
            {
                DeleteTempIfPresent(tmp);
            }
        }

        private static string CreateTempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

        private static void ReplaceWithTemp(string tmpPath, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    ReplaceExisting(tmpPath, targetPath);
                    return;
                }

                File.Move(tmpPath, targetPath);
            }
            catch (IOException) when (File.Exists(targetPath) && File.Exists(tmpPath))
            {
                // Another writer may have created the target between Exists and Move.
                ReplaceExisting(tmpPath, targetPath);
            }
        }

        private static void ReplaceExisting(string tmpPath, string targetPath)
        {
            IOException? lastError = null;
            for (int attempt = 1; attempt <= ReplaceAttempts; attempt++)
            {
                try
                {
                    File.Replace(tmpPath, targetPath, destinationBackupFileName: null);
                    return;
                }
                catch (IOException ex) when (attempt < ReplaceAttempts && File.Exists(tmpPath))
                {
                    lastError = ex;
#pragma warning disable CIVIC015 // Synchronous file replace retry; no async context exists in this low-level atomic writer.
                    Thread.Sleep(attempt * 10);
#pragma warning restore CIVIC015
                }
            }

            throw lastError ?? new IOException($"Atomic replace failed for {targetPath}");
        }

        private static void DeleteTempIfPresent(string tmpPath)
        {
            try
            {
                File.Delete(tmpPath);
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.WriteLine(ex);
            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine(ex);
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}
