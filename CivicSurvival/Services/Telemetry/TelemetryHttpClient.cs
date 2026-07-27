using System;
using System.Text;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// HTTP transport for telemetry payloads.
    /// Uses HttpUtils for timeout and retry support.
    /// </summary>
    public sealed class TelemetryHttpClient
    {
        private const int IMMEDIATE_TIMEOUT_MS = 5000;
        public const int MAX_REQUEST_BYTES = 512 * 1024;

        private static readonly LogContext Log = new("TelemetryHttpClient");
        private readonly TelemetryConfig m_Config;

        public TelemetryHttpClient(TelemetryConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void SendAsync(
            string payloadJson,
            string authToken,
            int eventCount,
            Action onSuccess = null!,
            Action<string> onFailure = null!,
            Action? onFinished = null)
        {
            if (string.IsNullOrEmpty(payloadJson))
            {
                Log.Warn(" Telemetry send skipped: empty payload");
                onFailure?.Invoke(payloadJson ?? "");
                return;
            }

            if (Encoding.UTF8.GetByteCount(payloadJson) > MAX_REQUEST_BYTES)
            {
                Log.Warn(" Telemetry send skipped: payload exceeds request cap");
                onFailure?.Invoke(payloadJson);
                return;
            }

            if (string.IsNullOrWhiteSpace(authToken))
            {
                Log.Warn(" Telemetry send skipped: missing auth token");
                onFailure?.Invoke(payloadJson);
                return;
            }

            var url = m_Config.ServerUrl + "/telemetry";

            HttpUtils.PostAsync(
                url,
                payloadJson,
                authToken,
                timeoutMs: m_Config.HttpTimeoutMs,
                maxRetries: m_Config.HttpMaxRetries,
                onComplete: result =>
                {
                    if (result.Success)
                    {
                        if (Log.IsDebugEnabled) Log.Debug($" Sent {eventCount} events (attempts={result.AttemptsUsed})");
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        Log.Warn($" HTTP failed after {result.AttemptsUsed} attempts: {result.ErrorMessage}");
                        onFailure?.Invoke(payloadJson);
                    }
                },
                onFinished: onFinished);
        }

        /// <summary>
        /// Anonymous functional-tier crash counter. Posts only { ModVersion, Marker } to
        /// /crash-rate with NO auth token, so the request sends no Authorization header and
        /// carries no player identity; the server stores only an aggregate per-version/marker
        /// count. Best-effort: short timeout, no retry, fire-and-forget. Gated by the caller
        /// on Online (NOT the diagnostics opt-in) — it is the whole-Online-audience signal.
        /// </summary>
        public void SendCrashRate(string modVersion, string marker, string exceptionCode, string phase)
        {
            var json = JsonBuilder.Object()
                .Add("ModVersion", modVersion ?? string.Empty)
                .Add("Marker", marker ?? string.Empty)
                // Raw observations, not a verdict: let the aggregate be sliced ANR (0x0517A7ED) vs
                // real crash, and load/save false-ANR vs in-game freeze, on the dashboard. Empty when
                // no dump was parsed / no heartbeat was recovered.
                .Add("ExceptionCode", exceptionCode ?? string.Empty)
                .Add("Phase", phase ?? string.Empty)
                .Build();

            var url = m_Config.ServerUrl + "/crash-rate";

            HttpUtils.PostAsync(
                url,
                json,
                authToken: string.Empty, // anonymous: no Authorization header, no player linkage
                timeoutMs: IMMEDIATE_TIMEOUT_MS,
                maxRetries: 0,
                onComplete: result =>
                {
                    if (!result.Success && Log.IsDebugEnabled)
                        Log.Debug($" Crash-rate send failed: {result.ErrorMessage}");
                });
        }

        public void TrySendImmediate(TelemetryPayload payload, string authToken, Action? onSuccess = null)
        {
            if (payload == null)
            {
                Log.Warn(" Immediate send skipped: missing payload");
                return;
            }

            if (string.IsNullOrWhiteSpace(authToken))
            {
                Log.Warn(" Immediate send skipped: missing auth token");
                return;
            }

            var serializer = TelemetryJsonSerializer.Instance;
            if (serializer == null)
            {
                Log.Warn(" Immediate send skipped: serializer unavailable");
                return;
            }

            var json = serializer.SerializePayload(payload);
            if (Encoding.UTF8.GetByteCount(json) > MAX_REQUEST_BYTES)
            {
                Log.Warn(" Immediate send skipped: payload exceeds request cap");
                return;
            }

            var url = m_Config.ServerUrl + "/immediate";

            // Immediate sends: shorter timeout, no retry (best-effort)
            HttpUtils.PostAsync(
                url,
                json,
                authToken,
                timeoutMs: IMMEDIATE_TIMEOUT_MS, // 5 second timeout for immediate
                maxRetries: 0,   // No retry for immediate
                onComplete: result =>
                {
                    if (result.Success)
                    {
                        if (Log.IsDebugEnabled) Log.Debug(" Immediate send succeeded");
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        if (Log.IsDebugEnabled) Log.Debug($" Immediate send failed: {result.ErrorMessage}");
                    }
                });
        }
    }
}
