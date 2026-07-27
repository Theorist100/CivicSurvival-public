using System;
using System.IO;
using System.Text;
using System.Threading;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;

namespace CivicSurvival.Services.RemoteConfig
{
    /// <summary>
    /// Fetches balance config from server with local cache fallback.
    /// Implements "Local Cache First" pattern:
    /// 1. Load from local balance_config.json (shipped with mod)
    /// 2. Check server for newer version
    /// 3. If newer - download and overwrite local file
    ///
    /// Refresh lifecycle (S073):
    /// - Construction publishes a bootstrap config synchronously via
    ///   <see cref="BalanceConfig.SetConfig"/>: embedded defaults first, then
    ///   <see cref="LoadLocalConfig"/> if a valid local file exists. From this point
    ///   <see cref="BalanceConfig.Current"/> is the single source of truth; consumers
    ///   (e.g. <c>CityDebtTrackingSystem</c>) must read from it directly, not cache
    ///   <see cref="RemoteBalanceConfig"/> through ServiceRegistry.
    /// - <see cref="Refresh"/> is the only entry point for remote pulls and is gated by
    ///   <c>TelemetryConfig.FileOnlyMode</c>:
    ///     - <c>FileOnlyMode == true</c> → file-only until persisted settings hydration completes
    ///       and the opt-in toggle flips. No HTTP traffic, <see cref="IsRemoteLoaded"/> stays false.
    ///     - <c>FileOnlyMode == false</c> → background fetch on first <see cref="Refresh"/> call
    ///       (typically post-hydration). Successful responses re-publish through
    ///       <see cref="BalanceConfig.SetConfig"/>; consumers automatically observe the new values
    ///       on their next read.
    /// - Re-publication is atomic from the consumer side: <see cref="BalanceConfig.Current"/>
    ///   is a lock-free <see cref="Volatile.Read"/>, so consumers never see a torn config.
    ///   Stale HTTP responses are rejected via <c>m_ConfigVersion</c> (monotonic counter) — a
    ///   newer refresh that completes first wins.
    /// - <see cref="Shutdown"/> stops further refresh scheduling. In-flight HTTP calls finish
    ///   on the background thread but their responses are dropped (<see cref="m_IsShutDown"/>).
    /// </summary>
    public enum RemoteConfigSource
    {
        Unknown = 0,
        DefaultEmbedded,
        LocalFile,
        RemoteServer
    }

    public sealed class RemoteConfigService
    {
        private static readonly LogContext Log = new("RemoteConfig");

        private const string CONFIG_FILENAME = ModPaths.BalanceConfigFile;
        private const long MAX_LOCAL_CONFIG_BYTES = 5L * 1024L * 1024L;
        private static readonly char[] s_PrereleaseSeparator = { '-' };
        private static readonly char[] s_PrereleasePartSeparator = { '.' };

        private readonly TelemetryConfig m_TelemetryConfig;
        private readonly string m_LocalConfigPath;

        // FIX MED-02: Thread safety for config accessed from main thread but written from ThreadPool
        private readonly object m_ConfigLock = new();
        private RemoteBalanceConfig m_Config;
        private RemoteConfigSource m_ConfigSource;
        private volatile bool m_IsRemoteLoaded;
        private volatile bool m_IsShutDown;
        [CivicSurvival.Core.Attributes.AsyncRequestGeneration("Async request generation used only to reject stale remote-config responses.")]
        private int m_ConfigVersion;  // Monotonic counter to reject stale responses
        private int m_ActiveRefreshes;

        public string CurrentVersion { get { lock (m_ConfigLock) return m_Config?.Version ?? "unknown"; } }
        public RemoteConfigSource CurrentSource { get { lock (m_ConfigLock) return m_ConfigSource; } }
        public bool IsRemoteLoaded => m_IsRemoteLoaded;

        public RemoteConfigService(TelemetryConfig telemetryConfig)
        {
            m_TelemetryConfig = telemetryConfig;

            // Local config path: mod data directory
            m_LocalConfigPath = Path.Combine(telemetryConfig.ModDataDirectory, CONFIG_FILENAME);

            // Publish defaults first so callers never see null Current.
#pragma warning disable CIVIC114 // Constructor init — no concurrent threads yet
            m_Config = BalanceConfig.SetConfig(new RemoteBalanceConfig());
            m_ConfigSource = RemoteConfigSource.DefaultEmbedded;
#pragma warning restore CIVIC114

            // Bootstrap gates are immutable for the world lifetime, so the local file must be
            // parsed before Mod.OnLoad builds FeatureManifest.
            LoadLocalConfig();
        }

        /// <summary>
        /// Returns shared validated config reference. Callers MUST NOT mutate it.
        /// </summary>
        public RemoteBalanceConfig GetConfig() { lock (m_ConfigLock) return m_Config; }

        /// <summary>
        /// Trigger refresh on background thread (fire-and-forget by design).
        /// </summary>
        public void Refresh()
        {
            if (m_TelemetryConfig.FileOnlyMode)
            {
                Log.Debug(" File-only mode, skipping server check");
                return;
            }

            if (m_IsShutDown)
                return;

            Interlocked.Increment(ref m_ActiveRefreshes);
            if (m_IsShutDown)
            {
                Interlocked.Decrement(ref m_ActiveRefreshes);
                return;
            }

            BackgroundTask.Run(() =>
            {
                if (!m_IsShutDown)
                    CheckAndFetchFromServer();
            }, () => Interlocked.Decrement(ref m_ActiveRefreshes));
        }

        private void LoadLocalConfig()
        {
            if (m_IsShutDown) return;

#pragma warning disable CIVIC064 // File.Exists needed: ReadAllText and FileInfo throw on missing file
            if (!File.Exists(m_LocalConfigPath))
#pragma warning restore CIVIC064
            {
                Log.Info(" No local config on disk; using default embedded config");
                return;
            }

            try
            {
                var fi = new FileInfo(m_LocalConfigPath);
                if (fi.Length > MAX_LOCAL_CONFIG_BYTES)
                {
                    Log.Error($"Config file too large ({fi.Length} bytes), deleting");
#pragma warning disable CIVIC143 // FP: Delete is in guard clause that returns before Read
                    try { File.Delete(m_LocalConfigPath); }
#pragma warning restore CIVIC143
                    catch (IOException) { /* Best-effort cleanup — file may be locked */ }
                    catch (UnauthorizedAccessException) { /* Best-effort cleanup — no permission */ }
                    return;
                }

#pragma warning disable CIVIC028 // FileInfo.Length guard above
                var json = File.ReadAllText(m_LocalConfigPath, Encoding.UTF8);
#pragma warning restore CIVIC028
                var parsed = BalanceConfigReader.Parse(json);

                if (m_IsShutDown) return;

                // Version-gate the on-disk cache against the embedded build. The cache is written only
                // by a prior successful remote-fetch, so it is worth applying ONLY when it is strictly
                // newer than the embedded config. A rebuild ships fresh contract values in the embedded
                // config; a cache that is equal or older carries nothing newer and must not shadow the
                // build. This is what makes "edit balance.contract.yaml + rebuild" reach the game without
                // hand-clearing ModData — a stale cache is ignored instead of silently overriding every
                // rebuild (the bug that made runtime SizeFactorMult diverge from the repo, 2026-06-23).
                string embeddedVersion;
                lock (m_ConfigLock) { embeddedVersion = m_Config.Version; }
                if (CompareSemanticVersions(parsed.Version, embeddedVersion) <= 0)
                {
                    Log.Info($"Local config cache v{parsed.Version} not newer than embedded v{embeddedVersion}; keeping embedded build config");
                    return;
                }

                var published = BalanceConfig.SetConfig(parsed);
                lock (m_ConfigLock)
                {
                    m_Config = published;
                    m_ConfigSource = RemoteConfigSource.LocalFile;
                }
                Log.Info($"Loaded local config cache v{published.Version} (newer than embedded v{embeddedVersion})");
            }
            catch (Exception ex)
            {
                Log.Error($"CONFIG CORRUPT — deleting {CONFIG_FILENAME}, keeping default config until server responds: {ex}");
#pragma warning disable CIVIC052 // Best-effort: error already logged in outer catch
                try { File.Delete(m_LocalConfigPath); } catch { /* best-effort cleanup */ }
#pragma warning restore CIVIC052
            }
        }

        public void Shutdown()
        {
            m_IsShutDown = true;
            var activeRefreshes = Interlocked.CompareExchange(ref m_ActiveRefreshes, 0, 0);
            if (activeRefreshes > 0 && Log.IsDebugEnabled)
                Log.Debug($"Shutdown with {activeRefreshes} remote config refresh(es) in flight");
        }

        private void CheckAndFetchFromServer()
        {
            if (m_IsShutDown) return;

            var versionUrl = $"{m_TelemetryConfig.ServerUrl}/config/balance/version";

            var result = HttpUtils.Get(
                versionUrl,
                authToken: "",
                timeoutMs: m_TelemetryConfig.HttpTimeoutMs,
                maxRetries: 1); // Single retry for version check

            if (!result.Success)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Server check failed: {result.ErrorMessage}");
                return;
            }

            try
            {
                var serverVersion = JsonStream.TryReadStringField(result.Response, "Version") ?? "";

                if (string.IsNullOrEmpty(serverVersion))
                {
                    Log.Debug(" Server version check failed - empty response");
                    return;
                }

                // Compare versions (lock for thread-safe read)
                string localVersion;
                lock (m_ConfigLock)
                {
                    localVersion = m_Config.Version;
                }

                var versionCompare = CompareSemanticVersions(serverVersion!, localVersion);
                if (versionCompare == 0)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Config up to date (v{serverVersion})");
                    return;
                }

                if (versionCompare < 0)
                {
                    Log.Warn($"Server config v{serverVersion} is older than local v{localVersion}; skipping remote config");
                    return;
                }

                // Fetch full config (capture version for stale-response rejection)
                int fetchVersion;
                lock (m_ConfigLock) { fetchVersion = ++m_ConfigVersion; }
                Log.Info($"New version available: {localVersion} -> {serverVersion}");
                FetchFullConfig(fetchVersion);
            }
            catch (Exception ex)
            {
                Log.Warn($"Version parse error: {ex}");
            }
        }

        private void FetchFullConfig(int expectedVersion)
        {
            var configUrl = $"{m_TelemetryConfig.ServerUrl}/config/balance";

            var result = HttpUtils.Get(
                configUrl,
                authToken: "",
                timeoutMs: m_TelemetryConfig.HttpTimeoutMs,
                maxRetries: m_TelemetryConfig.HttpMaxRetries);

            if (!result.Success)
            {
                if (!m_IsShutDown)
                    Mod.Log.Warn($"[RemoteConfig] Full config fetch failed: {result.ErrorMessage}");
                return;
            }

            try
            {
                var config = BalanceConfigReader.Parse(result.Response);

                if (!string.IsNullOrEmpty(config.Version))
                {
                    if (m_IsShutDown) return;

                    // TS-005 FIX: Thread-safe config update (flag inside lock to ensure atomicity)
                    bool staleResponse = false;
                    int staleCurrentVersion = 0;
                    lock (m_ConfigLock)
                    {
                        // Reject stale response (a newer fetch already completed)
                        if (m_IsShutDown || m_ConfigVersion != expectedVersion)
                        {
                            staleResponse = true;
                            staleCurrentVersion = m_ConfigVersion;
                        }
                        else
                        {
                            m_Config = BalanceConfig.SetConfig(config);
                            m_ConfigSource = RemoteConfigSource.RemoteServer;

                            // Disk cache is part of the same stale-response contract as in-memory state.
                            SaveToLocalFile(result.Response);
                            m_IsRemoteLoaded = true;
                        }
                    }

                    if (staleResponse)
                    {
                        if (Log.IsDebugEnabled) Log.Debug($"Stale config response (expected v{expectedVersion}, current v{staleCurrentVersion}) — discarding");
                        return;
                    }

                    Mod.Log.Info($"[RemoteConfig] Updated to v{config.Version}");
                }
            }
            catch (Exception ex)
            {
                if (!m_IsShutDown)
                    Mod.Log.Warn($"[RemoteConfig] Config parse error: {ex}");
            }
        }

        private void SaveToLocalFile(string json)
        {
            try
            {
                var dir = Path.GetDirectoryName(m_LocalConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                AtomicFileWriter.WriteAllText(m_LocalConfigPath, json, Encoding.UTF8);
                if (Log.IsDebugEnabled) Log.Debug($"Saved to {m_LocalConfigPath}");
            }
            catch (Exception ex)
            {
                Mod.Log.Warn($"[RemoteConfig] Failed to save local config: {ex}");
            }
        }

        private static int CompareSemanticVersions(string serverVersion, string localVersion)
        {
            var normalizedServer = serverVersion?.Trim() ?? "";
            var normalizedLocal = localVersion?.Trim() ?? "";
            if (string.Equals(normalizedServer, normalizedLocal, StringComparison.OrdinalIgnoreCase))
                return 0;

            var serverValid = TryParseSemanticVersion(normalizedServer, out var serverParts, out var serverPrerelease);
            var localValid = TryParseSemanticVersion(normalizedLocal, out var localParts, out var localPrerelease);
            if (!serverValid || !localValid)
            {
                if (serverValid && !localValid)
                    return 1;
                if (!serverValid && localValid)
                    return -1;
                return 0;
            }

            for (int i = 0; i < Math.Max(serverParts.Length, localParts.Length); i++)
            {
                int serverPart = i < serverParts.Length ? serverParts[i] : 0;
                int localPart = i < localParts.Length ? localParts[i] : 0;
                if (serverPart != localPart)
                    return serverPart.CompareTo(localPart);
            }

            return ComparePrerelease(serverPrerelease, localPrerelease);
        }

        private static bool TryParseSemanticVersion(string value, out int[] parts, out string[] prerelease)
        {
            parts = Array.Empty<int>();
            prerelease = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var withoutBuild = value.Trim().TrimStart('v', 'V').Split('+')[0];
            var versionAndPrerelease = withoutBuild.Split(s_PrereleaseSeparator, 2);
            var core = versionAndPrerelease[0];
            if (versionAndPrerelease.Length > 1)
            {
                prerelease = versionAndPrerelease[1]
                    .Split(s_PrereleasePartSeparator, StringSplitOptions.RemoveEmptyEntries);
            }

            var rawParts = core.Split('.');
            if (rawParts.Length == 0)
                return false;

            var parsed = new int[rawParts.Length];
            for (int i = 0; i < rawParts.Length; i++)
            {
                if (!int.TryParse(rawParts[i], out parsed[i]))
                    return false;
            }

            parts = parsed;
            return true;
        }

        private static int ComparePrerelease(string[] serverPrerelease, string[] localPrerelease)
        {
            var serverIsStable = serverPrerelease.Length == 0;
            var localIsStable = localPrerelease.Length == 0;
            if (serverIsStable && localIsStable) return 0;
            if (serverIsStable) return 1;
            if (localIsStable) return -1;

            for (int i = 0; i < Math.Max(serverPrerelease.Length, localPrerelease.Length); i++)
            {
                if (i >= serverPrerelease.Length) return -1;
                if (i >= localPrerelease.Length) return 1;

                var serverPart = serverPrerelease[i];
                var localPart = localPrerelease[i];
                var serverNumeric = int.TryParse(serverPart, out var serverNumber);
                var localNumeric = int.TryParse(localPart, out var localNumber);

                if (serverNumeric && localNumeric)
                {
                    var numberCompare = serverNumber.CompareTo(localNumber);
                    if (numberCompare != 0) return numberCompare;
                    continue;
                }

                if (serverNumeric != localNumeric)
                    return serverNumeric ? -1 : 1;

                var textCompare = string.Compare(serverPart, localPart, StringComparison.OrdinalIgnoreCase);
                if (textCompare != 0) return textCompare;
            }

            return 0;
        }

    }
}
