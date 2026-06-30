using System;
using System.IO;
using System.Security.Cryptography;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Services.Arena
{
    internal static class PendingArenaDataExtensions
    {
        public static bool HasData(this PendingArenaData data)
            => data.DamageDealt > 0 || data.ShadowSpent > 0 || data.FloorHit || data.StreakBroken || data.VulnerableHits > 0;
    }

    /// <summary>
    /// Disk persistence for Arena data.
    /// Ensures no data loss on crash before server upload.
    /// </summary>
    public sealed class ArenaPersistence
    {
        private const int MAX_FILE_SIZE = 1_048_576;
        private const int CURRENT_SCHEMA_VERSION = 1;

        private static readonly LogContext Log = new("ArenaPersistence");
        private const string PENDING_FILENAME = ModPaths.ArenaPendingFile;

        private readonly string m_FilePath;
        private readonly string m_RecoveringPath;
        private readonly object m_Lock = new();

        public ArenaPersistence(TelemetryConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            m_FilePath = Path.Combine(config.ModDataDirectory, PENDING_FILENAME);
            m_RecoveringPath = m_FilePath + ".recovering";
            EnsureDirectoryExists(config.ModDataDirectory);
        }

        /// <summary>
        /// Save pending Arena data to disk.
        /// Called periodically and before shutdown.
        /// </summary>
        public ArenaPendingGeneration Save(PendingArenaData data)
        {
            if (data == null || !data.HasData()) return ArenaPendingGeneration.None;

            try
            {
                var json = SerializeData(data);
                lock (m_Lock)
                {
                    AtomicFileWriter.WriteAllText(m_FilePath, json);
                    return CaptureGenerationUnsafe(m_FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error($" Save failed: {ex}");
                return ArenaPendingGeneration.None;
            }
        }

        /// <summary>
        /// Load and clear pending Arena data from previous session.
        /// Returns null if no pending data exists.
        /// </summary>
        public PendingArenaData? Recover()
        {
            try
            {
                string json;
                lock (m_Lock)
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose — protecting file from concurrent access
                    string? path = SelectRecoverablePathUnsafe();
                    if (path == null)
                    {
                        return null;
                    }
                    var fi = new FileInfo(path);
                    if (fi.Length > MAX_FILE_SIZE) // 1 MB guard — arena data is <10 KB
                    {
                        Log.Warn($"Arena file too large ({fi.Length} bytes), deleting");
#pragma warning disable CIVIC143 // FP: Delete is in guard clause that returns before Read
                        File.Delete(path);
#pragma warning restore CIVIC143
                        return null;
                    }
#pragma warning disable CIVIC028 // FileInfo.Length guard above, recovering file is the same checked file
                    json = File.ReadAllText(path);
#pragma warning restore CIVIC028
#pragma warning restore CIVIC147
                }

                var data = DeserializeData(json);

                if (data != null && data.HasData())
                {
                    Log.Info($" Recovered: damage={data.DamageDealt}, shadow={data.ShadowSpent}, floor={data.FloorHit}, streak={data.StreakBroken}, vulnerable={data.VulnerableHits}");
                    return data;
                }
                MovePendingToRecovering();
                Log.Warn(" Pending arena file was empty or invalid; moved to .recovering for diagnostics");
            }
            catch (FileNotFoundException)
            {
                // File was deleted between iterations - not an error
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn($" Recovery failed: {ex}");
                MovePendingToRecovering();
            }

            return null;
        }

        /// <summary>
        /// Clear pending data after successful server upload.
        /// </summary>
        public void Clear()
        {
            Clear(ArenaPendingGeneration.Any);
        }

        /// <summary>
        /// Clear pending data after successful server upload, but only if the
        /// acknowledged generation is still the durable generation on disk.
        /// </summary>
        public bool Clear(ArenaPendingGeneration generation)
        {
            return TryDeleteFile(generation);
        }

        /// <summary>
        /// Update existing pending data (accumulate).
        /// </summary>
        public void Accumulate(int damage, int shadow, int vulnerable, bool floor, bool broken)
        {
            try
            {
                lock (m_Lock)
                {
                    var existing = LoadWithoutDeleteUnsafe() ?? new PendingArenaData();

                    existing.DamageDealt = SaturatingAdd(existing.DamageDealt, damage);
                    existing.ShadowSpent = SaturatingAdd(existing.ShadowSpent, shadow);
                    existing.VulnerableHits = SaturatingAdd(existing.VulnerableHits, vulnerable);
                    existing.FloorHit |= floor;
                    existing.StreakBroken |= broken;
                    existing.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    AtomicFileWriter.WriteAllText(m_FilePath, SerializeData(existing));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Arena.Accumulate] Failed: {ex}");
            }
        }

        private PendingArenaData? LoadWithoutDeleteUnsafe()
        {
            try
            {
#pragma warning disable CIVIC147 // File IO is the lock's purpose — protecting file from concurrent access
                if (!File.Exists(m_FilePath)) return null;
                var fi = new FileInfo(m_FilePath);
                if (fi.Length > MAX_FILE_SIZE) return null; // 1 MB guard
#pragma warning disable CIVIC028 // FileInfo.Length guard above
                string json = File.ReadAllText(m_FilePath);
#pragma warning restore CIVIC028
#pragma warning restore CIVIC147
                return DeserializeData(json);
            }
            catch (System.Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"TryLoadLatest failed: {ex}");
                return null;
            }
        }

        public ArenaPendingGeneration CaptureCurrentGeneration()
        {
            lock (m_Lock)
            {
                return CaptureGenerationUnsafe(m_FilePath);
            }
        }

        private bool TryDeleteFile(ArenaPendingGeneration generation)
        {
            try
            {
                lock (m_Lock)
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose
                    bool deleted = TryDeleteGenerationUnsafe(m_FilePath, generation);
                    deleted |= TryDeleteGenerationUnsafe(m_RecoveringPath, generation);
                    return deleted;
#pragma warning restore CIVIC147
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Arena.Delete] Failed: {ex}");
                return false;
            }
        }

        private static bool TryDeleteGenerationUnsafe(string path, ArenaPendingGeneration generation)
        {
            if (!File.Exists(path))
                return false;
            if (!generation.Matches(CaptureGenerationUnsafe(path)))
                return false;
            File.Delete(path);
            return true;
        }

        private void MovePendingToRecovering()
        {
            try
            {
                lock (m_Lock)
                {
#pragma warning disable CIVIC147 // File IO is the lock's purpose
                    if (!File.Exists(m_FilePath))
                        return;

                    File.Delete(m_RecoveringPath);
                    File.Move(m_FilePath, m_RecoveringPath);
#pragma warning restore CIVIC147
                }
            }
            catch (Exception moveEx)
            {
                Log.Warn($"[Arena.Recover] Failed to move invalid pending file: {moveEx}");
            }
        }

        private string? SelectRecoverablePathUnsafe()
        {
            if (File.Exists(m_FilePath))
                return m_FilePath;
            if (File.Exists(m_RecoveringPath))
                return m_RecoveringPath;
            return null;
        }

        private static ArenaPendingGeneration CaptureGenerationUnsafe(string path)
        {
            if (!File.Exists(path))
                return ArenaPendingGeneration.None;

            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return new ArenaPendingGeneration(Convert.ToBase64String(sha256.ComputeHash(stream)));
        }

        private static void EnsureDirectoryExists(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception ex)
            {
                Log.Error($" Failed to create directory: {ex}");
            }
        }

        private static string SerializeData(PendingArenaData data)
        {
            return ArenaWireWriter.BuildPendingArenaDataJson(data);
        }

        private static PendingArenaData? DeserializeData(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var data = ArenaWireReader.ParsePendingArenaData(json);
            if (data != null && data.SchemaVersion != CURRENT_SCHEMA_VERSION)
            {
                Log.Warn($" Unsupported arena pending schema v{data.SchemaVersion}; expected v{CURRENT_SCHEMA_VERSION}");
                return null;
            }
            return data;
        }

        private static int SaturatingAdd(int current, int delta)
        {
            if (delta <= 0) return current;
            long next = (long)current + delta;
            return next >= int.MaxValue ? int.MaxValue : checked((int)next);
        }
    }

    public readonly struct ArenaPendingGeneration
    {
        public static readonly ArenaPendingGeneration None = new("");
        public static readonly ArenaPendingGeneration Any = new("*");

        public ArenaPendingGeneration(string contentHash)
        {
            ContentHash = contentHash ?? "";
        }

        public string ContentHash { get; }
        public bool IsAny => ContentHash == "*";
        public bool IsPresent => IsAny || ContentHash.Length > 0;

        public bool Matches(ArenaPendingGeneration other)
            => IsAny || (IsPresent && ContentHash == other.ContentHash);
    }
}
