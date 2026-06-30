using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Process-owned telemetry identity. Keeps persistent player identity separate
    /// from optional server auth session and prevents per-subsystem first-run races.
    /// </summary>
    [InfrastructureService]
    public sealed class TelemetryIdentityService
    {
        private readonly object m_Lock = new();
        private TelemetryConfig m_Config;
        private TelemetryAuth m_Auth;

        public TelemetryIdentityService(TelemetryConfig config)
        {
            m_Config = config ?? throw new ArgumentNullException(nameof(config));
            m_Auth = CreateAuth(m_Config);
        }

        public TelemetryAuth GetAuth(TelemetryConfig config)
        {
            lock (m_Lock)
            {
                if (ShouldReload(config))
                {
                    m_Config = config ?? throw new ArgumentNullException(nameof(config));
                    m_Auth = CreateAuth(m_Config);
                }

                return m_Auth;
            }
        }

        private bool ShouldReload(TelemetryConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // OnlineEnabled is the auth-relevant signal (server identity is gated by
            // Online, not by diagnostics). A FileOnlyMode change no longer affects
            // whether auth can register, so it must not force an auth reload.
            return !string.Equals(m_Config.ModDataDirectory, config.ModDataDirectory, StringComparison.Ordinal)
                || !string.Equals(m_Config.ServerUrl, config.ServerUrl, StringComparison.Ordinal)
                || m_Config.OnlineEnabled != config.OnlineEnabled;
        }

        private static TelemetryAuth CreateAuth(TelemetryConfig config)
        {
            var auth = new TelemetryAuth(config);
            auth.LoadOrCreateCredentials();
            return auth;
        }
    }
}
