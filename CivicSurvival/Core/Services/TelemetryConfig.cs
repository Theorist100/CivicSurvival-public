using System;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Configuration DTO for Telemetry subsystem.
    /// Immutable after creation — pass to subsystem constructors.
    /// </summary>
    public sealed class TelemetryConfig
    {
        // Server URLs
#pragma warning disable S1075 // Hardcoded URIs - server configuration by design
#pragma warning disable S1144, CA1823 // Unused field - used in Release build via #if
        private const string DEV_SERVER_URL = "http://localhost:9000/api";

        /// <summary>
        /// Canonical production telemetry/server host — single source of truth for the PROD
        /// URL across every network callsite (telemetry config, news polling, chronicle).
        /// Duplicating this literal per callsite is what let the TLS-enforcement invariant
        /// diverge: use this constant + <see cref="NormalizeServerUrl"/> everywhere.
        /// </summary>
        public const string ProductionServerUrl = "https://api.civicsurvival.com/api";
#pragma warning restore S1144, CA1823
#pragma warning restore S1075

        private const string DEFAULT_SERVER_URL = ProductionServerUrl;

        /// <summary>
        /// Whether anonymous diagnostics actually run. This is the EFFECTIVE gate:
        /// <c>diagnostics-opt-in &amp;&amp; Online</c>. Diagnostics (batch send, crash
        /// detection, Sentry, pulse) are analytics for the developer and only flow while
        /// the player is connected (Online) AND has not opted out — turning Online off
        /// stops diagnostics even if the opt-in flag is still true. Do NOT confuse with
        /// <see cref="OnlineEnabled"/>, which gates functional server identity
        /// (registration / auth_token) on Online alone, independently of diagnostics.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Raw anonymous-diagnostics opt-in (env / global opt-in store / settings),
        /// BEFORE the Online gate is applied. Persisted player choice. <see cref="Enabled"/>
        /// is this AND <see cref="OnlineEnabled"/>; this property is kept so the effective
        /// gate can be recomputed when Online toggles without re-reading the opt-in.
        /// </summary>
        public bool DiagnosticsOptIn { get; }

        /// <summary>
        /// Whether the player has opted into the online connection (Global Grid).
        /// Gates functional server identity — player registration (auth_token),
        /// nickname, leaderboards — independently of <see cref="Enabled"/> (diagnostics).
        /// </summary>
        public bool OnlineEnabled { get; }

        /// <summary>
        /// Server URL for telemetry API.
        /// </summary>
#pragma warning disable CA1056 // String instead of Uri - simpler for HTTP clients
        public string ServerUrl { get; }
#pragma warning restore CA1056

        /// <summary>
        /// If true, only write to disk (no HTTP).
        /// Useful for testing without server.
        /// </summary>
        public bool FileOnlyMode { get; }

        /// <summary>
        /// Interval between batch sends in seconds.
        /// </summary>
        public float SendIntervalSeconds { get; }

        /// <summary>
        /// Maximum events in a batch before forced send.
        /// </summary>
        public int MaxBatchSize { get; }

        /// <summary>
        /// Interval between performance metric samples in seconds.
        /// </summary>
        public float PerformanceSampleIntervalSeconds { get; }

        /// <summary>
        /// HTTP request timeout in milliseconds.
        /// </summary>
        public int HttpTimeoutMs { get; }

        /// <summary>
        /// Maximum retry attempts for failed HTTP requests.
        /// Uses exponential backoff between retries.
        /// </summary>
        public int HttpMaxRetries { get; }

        /// <summary>
        /// Base directory for telemetry files.
        /// </summary>
        public string LogsDirectory { get; }

        /// <summary>
        /// Directory for player credentials and mod data.
        /// </summary>
        public string ModDataDirectory { get; }

        private TelemetryConfig(
            bool diagnosticsOptIn,
            bool onlineEnabled,
            string serverUrl,
            float sendIntervalSeconds,
            int maxBatchSize,
            float performanceSampleIntervalSeconds,
            int httpTimeoutMs,
            int httpMaxRetries,
            string logsDirectory,
            string modDataDirectory)
        {
            DiagnosticsOptIn = diagnosticsOptIn;
            OnlineEnabled = onlineEnabled;
            // Effective diagnostics gate: opt-in AND Online. Online off → no diagnostics
            // even if the opt-in flag is set. FileOnlyMode is the inverse — when
            // diagnostics are off, nothing leaves over HTTP.
            Enabled = diagnosticsOptIn && onlineEnabled;
            FileOnlyMode = !Enabled;
            ServerUrl = serverUrl;
            SendIntervalSeconds = sendIntervalSeconds;
            MaxBatchSize = maxBatchSize;
            PerformanceSampleIntervalSeconds = performanceSampleIntervalSeconds;
            HttpTimeoutMs = httpTimeoutMs;
            HttpMaxRetries = httpMaxRetries;
            LogsDirectory = logsDirectory;
            ModDataDirectory = modDataDirectory;
        }

        /// <summary>
        /// Load configuration from ModSettings and environment variables.
        /// </summary>
        public static TelemetryConfig Load(ModSettings? settings)
        {
            var envEnabled = Environment.GetEnvironmentVariable("CIVIC_TELEMETRY_ENABLED") == "1";
            var settingsEnabled = false;
            if (settings != null)
                settingsEnabled = settings.TelemetryEnabled;
            // Global opt-in file is the init-time source of truth: it is readable before
            // the city save deserializes, so crash detection at OnCreate sees the real
            // consent state instead of the save default (false).
            var diagnosticsOptIn = envEnabled || settingsEnabled || TelemetryOptInStore.Read();

            // Online connection (functional identity gate) is independent of the opt-in.
            // Prefer the settings value (seeded from ConsentStore at boot/load), but fall
            // back to the global store directly for the earliest init-path where settings
            // is null and NetworkConnectionEnabled has not been seeded yet.
            var online = settings?.NetworkConnectionEnabled ?? ConsentStore.Read(ConsentKey.OnlineConnection);

            // Server URL: env var override or default
            var urlOverride = Environment.GetEnvironmentVariable("CIVIC_TELEMETRY_URL");
            var serverUrl = NormalizeServerUrl(urlOverride, DEFAULT_SERVER_URL);

            // Effective Enabled (= opt-in AND online) and FileOnlyMode are computed in the
            // constructor — Online off means no diagnostics even with the opt-in set.
            return new TelemetryConfig(
                diagnosticsOptIn: diagnosticsOptIn,
                onlineEnabled: online,
                serverUrl: serverUrl,
                sendIntervalSeconds: 300f,      // 5 minutes
                maxBatchSize: 100,
                performanceSampleIntervalSeconds: 60f,  // 1 minute
                httpTimeoutMs: 10000,           // 10 seconds
                httpMaxRetries: 3,              // 3 retries with exponential backoff
                logsDirectory: ModPaths.LogsDirectory,
                modDataDirectory: ModPaths.ModDataDirectory
            );
        }

        /// <summary>
        /// Return a copy of this config with <see cref="OnlineEnabled"/> set to the given
        /// authoritative value, all other fields preserved. Used when the Online state is
        /// carried by the post-write toggle event (OnlineConnectionStateChangedEvent): the
        /// config is loaded from settings for the durable fields (server URL, timeouts) but
        /// the online gate is taken from the event, not re-read from the setting the writer
        /// just patched in the same dispatch — keeping a single source for OnlineEnabled
        /// across the identity gate and the ArenaReporter report gate.
        ///
        /// <see cref="Enabled"/> (effective diagnostics) is recomputed from the preserved
        /// <see cref="DiagnosticsOptIn"/> AND the new online value, so toggling Online both
        /// brings up identity AND flips the diagnostics gate in one place.
        /// </summary>
        public TelemetryConfig WithOnlineEnabled(bool onlineEnabled)
        {
            if (onlineEnabled == OnlineEnabled)
                return this;

            return new TelemetryConfig(
                diagnosticsOptIn: DiagnosticsOptIn,
                onlineEnabled: onlineEnabled,
                serverUrl: ServerUrl,
                sendIntervalSeconds: SendIntervalSeconds,
                maxBatchSize: MaxBatchSize,
                performanceSampleIntervalSeconds: PerformanceSampleIntervalSeconds,
                httpTimeoutMs: HttpTimeoutMs,
                httpMaxRetries: HttpMaxRetries,
                logsDirectory: LogsDirectory,
                modDataDirectory: ModDataDirectory);
        }

        /// <summary>
        /// Create test configuration with custom values.
        /// </summary>
        public static TelemetryConfig ForTesting(
            bool enabled = true,
            int maxBatchSize = 10,
            float sendIntervalSeconds = 1f,
            string? logsDirectory = null,
            string? modDataDirectory = null,
            bool onlineEnabled = true)
        {
            // FileOnlyMode is derived (= !Enabled = !(opt-in && online)); not a constructor
            // input. To force HTTP-disabled in a test, pass enabled:false or onlineEnabled:false.
            return new TelemetryConfig(
                diagnosticsOptIn: enabled,
                onlineEnabled: onlineEnabled,
                serverUrl: DEV_SERVER_URL,
                sendIntervalSeconds: sendIntervalSeconds,
                maxBatchSize: maxBatchSize,
                performanceSampleIntervalSeconds: 10f,
                httpTimeoutMs: 5000,             // 5 seconds for tests
                httpMaxRetries: 1,               // 1 retry for tests
                logsDirectory: logsDirectory ?? ModPaths.LogsDirectory,
                modDataDirectory: modDataDirectory ?? ModPaths.ModDataDirectory
            );
        }

        /// <summary>
        /// Single source of TLS enforcement for any server URL, including env overrides.
        /// Returns <paramref name="overrideUrl"/> only if it is an absolute https URL (or,
        /// under DEBUG, an http loopback); otherwise falls back to <paramref name="fallback"/>.
        /// EVERY callsite that resolves a server URL — telemetry config, news polling,
        /// chronicle — must route through this so a <c>CIVIC_TELEMETRY_URL=http://…</c> can
        /// never downgrade the transport that carries player_id + auth_token.
        /// </summary>
#pragma warning disable CA1054, CA1055 // string URL by design — matches ServerUrl (CA1056); HTTP clients take strings.
        public static string NormalizeServerUrl(string? overrideUrl, string fallback)
#pragma warning restore CA1054, CA1055
        {
            if (string.IsNullOrWhiteSpace(overrideUrl))
                return fallback;

            var candidate = overrideUrl!.Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                return fallback;

            if (uri.Scheme == Uri.UriSchemeHttps)
                return candidate.TrimEnd('/');

#if DEBUG
            if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
                return candidate.TrimEnd('/');
#endif

            return fallback;
        }
    }
}
