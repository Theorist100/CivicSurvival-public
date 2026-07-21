using System.Diagnostics;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.RemoteConfig
{
    /// <summary>
    /// Pre-publish update announce: polls the server's <c>/config/announce</c> and warns players
    /// BEFORE a mod version is published, so they can save and restart on their own terms instead
    /// of having the Paradox SDK swap the install folder under their live session (see
    /// <c>ModInstanceWatchSystem</c> — the after-the-fact detector this announce complements).
    ///
    /// Flow: the release workflow sets an announce (publish time = now + 15/30 min) on the server;
    /// this system polls every <see cref="POLL_INTERVAL_SECONDS"/>, publishes the state to the UI
    /// (countdown badge in the dashboard tab bar, in place of the BETA badge) and raises the
    /// ModUpdatePending modal once per announced publish time.
    ///
    /// Consent gate: OnlineEnabled — same scope as the arena ladders and the anonymous crash
    /// counter. The announce request carries no player data (pure config GET, no auth), so it does
    /// NOT require the diagnostics opt-in; but with Online off nothing leaves the machine, so
    /// offline-by-choice players simply keep the second line of defense (the deletion detector).
    ///
    /// Poll cost: one small GET per 2 minutes per online player in a loaded city — negligible for
    /// this audience, and the endpoint is a cached-file read server-side.
    /// </summary>
    [ActIndependent]
    public partial class UpdateAnnounceSystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("UpdateAnnounce");

        private const double POLL_INTERVAL_SECONDS = 120.0;
        private const string INACTIVE_STATE_JSON = "{\"Active\":false,\"PublishAtUnix\":0}";

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        private ProfiledBinding<string> m_StateBinding = null!;

        private ModSettings? m_Settings;
        private Core.Services.TelemetryConfig? m_Config;
        private bool m_OnlineEnabled;
        // Announce endpoint URL, composed once (ServerUrl is immutable for the config lifetime) —
        // the poll path must not interpolate per tick.
        private string m_AnnounceUrl = string.Empty;

        // Wall-clock poll throttle (pause- and game-speed-independent). First poll fires on the
        // first throttled tick after load so an announce set minutes ago is seen immediately.
        private readonly Stopwatch m_Clock = Stopwatch.StartNew();
        private double m_NextPollSeconds;
        private int m_PollInFlight;

        // Background thread drops the freshest server answer here; main thread consumes it.
        // All access via Interlocked.Exchange (a volatile modifier would fight the ref-passing).
        private string? m_PendingStateJson;

        // Modal latch keyed by the announced publish time: one modal per announce, but a NEW
        // announce (different publish time) after the first is shown again.
        private long m_ModalShownForPublishAt;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_StateBinding = new ProfiledBinding<string>(B.Group, B.UpdateAnnounceState, INACTIVE_STATE_JSON);
            AddBinding(m_StateBinding.Binding);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            if (m_Settings != null)
                return;
            m_Settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = Core.Services.TelemetryConfig.Load(m_Settings);
            m_OnlineEnabled = m_Config.OnlineEnabled;
            m_AnnounceUrl = $"{m_Config.ServerUrl}/config/announce";
            if (!m_OnlineEnabled)
                Log.Info(" Disabled (online off) — update announces will not be polled");
        }

        protected override void OnThrottledUpdate()
        {
            PublishPendingState();

            if (!m_OnlineEnabled || m_Config == null)
                return;

            double now = m_Clock.Elapsed.TotalSeconds;
            if (now < m_NextPollSeconds)
                return;
            m_NextPollSeconds = now + POLL_INTERVAL_SECONDS;

            // Single flight: a slow/hung request must not stack a second one behind it.
            if (Interlocked.CompareExchange(ref m_PollInFlight, 1, 0) != 0)
                return;

            string url = m_AnnounceUrl;
            int timeoutMs = m_Config.HttpTimeoutMs;
            BackgroundTask.Run(() =>
            {
#pragma warning disable CIVIC022 // Not a per-frame allocation: this lambda runs once per
                // POLL_INTERVAL_SECONDS (120s wall-clock throttle above) on a background thread.
                var result = HttpUtils.Get(url, authToken: "", timeoutMs: timeoutMs, maxRetries: 1);
                if (!result.Success)
                {
                    if (Log.IsDebugEnabled)
                        Log.Debug($" Announce poll failed: {result.ErrorMessage}");
                    return;
                }

                bool active = JsonStream.TryReadBoolField(result.Response, "Active");
                long publishAt = JsonStream.TryReadLongField(result.Response, "PublishAtUnix");
                // Re-serialize canonically instead of forwarding the raw response: the UI binding
                // carries exactly two fields regardless of what the server adds later.
                Interlocked.Exchange(ref m_PendingStateJson, active && publishAt > 0
                    ? $"{{\"Active\":true,\"PublishAtUnix\":{publishAt}}}"
                    : INACTIVE_STATE_JSON);
#pragma warning restore CIVIC022
            }, () => Interlocked.Exchange(ref m_PollInFlight, 0));
        }

        /// <summary>
        /// Main-thread half: push the freshest server answer into the UI binding and raise the
        /// modal once per announced publish time.
        /// </summary>
        private void PublishPendingState()
        {
            string? json = Interlocked.Exchange(ref m_PendingStateJson, null);
            if (json == null)
                return;

            m_StateBinding.Update(json);

            long publishAt = JsonStream.TryReadLongField(json, "PublishAtUnix");
            if (publishAt <= 0 || publishAt == m_ModalShownForPublishAt)
                return;

            m_ModalShownForPublishAt = publishAt;
            Log.Info($" Update announce received — publishAtUnix={publishAt}; raising ModUpdatePending");
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
#pragma warning disable CIVIC239 // Best-effort notice: a busier slot queues it inside the coordinator;
            // the publish-time latch above makes this the only attempt per announce.
            ModalCoordinator.Instance.TryShow("ModUpdatePending", json);
#pragma warning restore CIVIC239
#pragma warning restore CIVIC098
        }
    }
}
