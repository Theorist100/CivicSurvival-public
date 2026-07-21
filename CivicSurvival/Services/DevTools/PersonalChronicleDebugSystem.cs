#if DEBUG
using System;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Systems;
using CivicSurvival.Localization;
using Newtonsoft.Json.Linq;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// DEBUG ONLY: manual trigger for the player's Personal AI Chronicle digest.
    ///
    /// The production path generates per-player digests on a ~30 min server worker;
    /// waiting that long while testing is painful, so this dev button POSTs
    /// <c>/chronicle/personal/generate</c> for the current player on demand. The
    /// server generates synchronously (idempotent per window — repeat clicks
    /// re-bill nothing), then this system kicks <see cref="PersonalChronicleSystem"/>
    /// into an immediate refetch so the new digest appears in the Herald feed.
    ///
    /// PAUSE-SAFETY (AXIOM 14): registered in UIUpdate (ticks while paused). The
    /// trigger callback only sets a flag; the network call fires from
    /// <see cref="OnThrottledUpdate"/> on a background thread, and the result is
    /// handed back to the main thread through a volatile latch — never through an
    /// ECB → GameSimulation consumer (which would be dead while paused).
    ///
    /// Identity is the durable player_id + auth_token from <see cref="TelemetryAuth"/>
    /// (same as the polling system), resolved through the shared telemetry config.
    /// </summary>
    [ActIndependent]
    public partial class PersonalChronicleDebugSystem : TriggeredThrottledUISystemBase
    {
        private static readonly LogContext Log = new("PersonalChronicleDebug");

        protected override int UpdateInterval => 30;

        // Server generates the digest synchronously inside the request: one LLM call
        // caps at 60 s plus a possible backoff retry. The default 10 s client timeout
        // closes the socket mid-generation, and the disconnect cancels the Starlette
        // handler (CancelledError) before reserve→generate→finalize commits, leaving
        // personal_chronicle empty. 120 s covers the LLM call plus a retry.
        private const int GENERATE_TIMEOUT_MS = 120000;

        // Trigger → throttled-update handoff (set on the UI trigger thread, read on
        // the main throttled update). volatile is enough — single producer/consumer flag.
        private volatile bool m_PendingGenerate;

        // In-flight guard so a second click while a request is outstanding does not
        // fan a duplicate POST (the server dedups anyway, but no point spending the round-trip).
        private int m_RequestInFlight;

        // Background → main-thread result latch. The background task writes the status
        // text once, then sets the flag; the next throttled update consumes it.
        private volatile string m_ResultText = string.Empty;
        private int m_ResultReady;

        private TelemetryConfig m_TelemetryConfig = null!;
        private TelemetryAuth m_Auth = null!;
        private string m_ServerUrl = string.Empty;

        private ProfiledBinding<string> m_StatusBinding = null!;

        protected override void ConfigureTriggers(TriggerRegistry triggers)
        {
            triggers.AddUngated(DebugGeneratePersonalChronicle, OnGenerateRequested);
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_StatusBinding = new ProfiledBinding<string>(Group, Debug_PersonalChronicleStatus, string.Empty);
            AddBinding(m_StatusBinding.Binding);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_TelemetryConfig = TelemetryConfig.Load(settings);
            m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_TelemetryConfig);

            // Route the env override through the single TLS-enforcing resolver.
            var urlOverride = Environment.GetEnvironmentVariable("CIVIC_TELEMETRY_URL");
            m_ServerUrl = TelemetryConfig.NormalizeServerUrl(urlOverride, TelemetryConfig.ProductionServerUrl);
        }

        private void OnGenerateRequested() => m_PendingGenerate = true;

        protected override void OnThrottledUpdate()
        {
            // Deliver any completed background result to the binding (main thread).
            if (Interlocked.Exchange(ref m_ResultReady, 0) != 0)
            {
                string status = m_ResultText;
                if (m_StatusBinding.Value != status)
                    m_StatusBinding.Update(status);

                // Pull the freshly generated digest into the feed without waiting for
                // the personal poller's 180 s cadence.
                if (status.StartsWith("generated", StringComparison.OrdinalIgnoreCase)
                    || status.StartsWith("exists", StringComparison.OrdinalIgnoreCase))
                {
                    World.GetExistingSystemManaged<PersonalChronicleSystem>()?.DebugRefetchNow();
                }
            }

            if (!m_PendingGenerate) return;
            m_PendingGenerate = false;

            if (Interlocked.CompareExchange(ref m_RequestInFlight, 1, 0) != 0)
            {
                // A request is already outstanding — report it instead of stacking another.
                SetStatus("pending: request in progress");
                return;
            }

            StartGenerateRequest();
        }

        private void StartGenerateRequest()
        {
            if (!m_TelemetryConfig.OnlineEnabled || !m_Auth.IsRegistered || string.IsNullOrEmpty(m_Auth.AuthToken))
            {
                SetStatus("error: not registered (online off or no auth token)");
                Volatile.Write(ref m_RequestInFlight, 0);
                return;
            }

            SetStatus("requesting...");

            // Snapshot identity on the main thread before crossing to the pool.
            string playerId = m_Auth.PlayerId;
            string authToken = m_Auth.AuthToken;
            string lang = LocalizationManager.CurrentLocale;
            string url = $"{m_ServerUrl}/chronicle/personal/generate";

            BackgroundTask.Run(() =>
            {
                try
                {
                    var body = JsonBuilder.Object()
                        .Add("player_id", playerId)
                        .Add("lang", lang)
                        .Build();

                    var result = HttpUtils.Post(url, body, authToken, timeoutMs: GENERATE_TIMEOUT_MS);
                    if (!result.Success)
                    {
                        PublishResult($"error: {result.StatusCode} {result.ErrorMessage}");
                        return;
                    }

                    string status = ParseStatus(result.Response);
                    PublishResult(DescribeStatus(status));
                }
                catch (Exception ex)
                {
                    Log.Error($"[DEBUG] Personal chronicle generate failed: {ex}");
                    PublishResult("error: request threw (see log)");
                }
            }, () => Volatile.Write(ref m_RequestInFlight, 0));
        }

        /// <summary>Hand a finished status string back to the main thread.</summary>
        private void PublishResult(string text)
        {
            m_ResultText = text;
            Interlocked.Exchange(ref m_ResultReady, 1);
        }

        /// <summary>Update the binding directly (main-thread callers only).</summary>
        private void SetStatus(string text)
        {
            if (m_StatusBinding.Value != text)
                m_StatusBinding.Update(text);
        }

        private static string ParseStatus(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                return root["status"]?.Type == JTokenType.String
                    ? root["status"]!.Value<string>() ?? string.Empty
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warn($"[DEBUG] Personal chronicle generate: unparseable response: {ex}");
                return string.Empty;
            }
        }

        // Server status string → human-readable line for the debug binding. Known
        // values only; anything else surfaces verbatim so an unexpected payload is visible.
        private static string DescribeStatus(string status)
        {
            if (string.Equals(status, "generated", StringComparison.OrdinalIgnoreCase))
                return "generated: new digest ready (refreshing feed)";
            if (string.Equals(status, "exists", StringComparison.OrdinalIgnoreCase))
                return "exists: already generated this window";
            if (string.Equals(status, "no_activity", StringComparison.OrdinalIgnoreCase))
                return "no activity to report this window";
            if (string.Equals(status, "disabled", StringComparison.OrdinalIgnoreCase))
                return "disabled: personal chronicle off on server";
            return $"unexpected server status: '{status}'";
        }
    }
}
#endif
