using System;
using System.Collections.Generic;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Bootstrap snapshot of feature gate state. Built once after
    /// <see cref="Services.RemoteConfig.RemoteConfigService"/> loads its local
    /// config; immutable for the lifetime of the world.
    ///
    /// Carries the wave-only feature availability snapshot from
    /// <see cref="FeatureGatesConfig"/>. A feature with wave greater than or
    /// equal to <see cref="FeatureWaveConstants.WAVE_SENTINEL_UNAVAILABLE"/> is
    /// unavailable for this build; a feature with a future wave below the
    /// sentinel is preview-only; a feature at or below the current wave runs.
    ///
    /// Background <c>RemoteConfigService.Refresh()</c> may update the local
    /// cache file for the NEXT launch but MUST NOT mutate the current
    /// manifest — changing feature availability after registration corrupts
    /// scheduling and breaks the closed-feature semantics invariant (§2.3).
    /// </summary>
    public sealed class FeatureManifest
    {
        private static readonly LogContext Log = new("FeatureManifest");

        private readonly IReadOnlyDictionary<string, int> m_Waves;
        private readonly int m_CurrentWave;

        private FeatureManifest(
            IReadOnlyDictionary<string, int> waves,
            int currentWave)
        {
            m_Waves = waves;
            m_CurrentWave = currentWave;
        }

        /// <summary>
        /// The rollout wave that unlocks this feature. Missing entry = wave 1
        /// (always reached). Mirrors the UI's <c>useBetaWave</c> default so
        /// backend and UI classify the same feature identically.
        /// </summary>
        public int WaveOf(string? featureId)
        {
            if (string.IsNullOrEmpty(featureId)) return 1;
            return m_Waves.TryGetValue(featureId!, out var wave) ? wave : 1;
        }

        /// <summary>The active rollout wave (1-based).</summary>
        public int CurrentWave => m_CurrentWave;

        /// <summary>
        /// True if the feature's rollout wave has been reached
        /// (<c>WaveOf(id) &lt;= CurrentWave</c>) and is below the unavailable
        /// sentinel. Sentinel waves remain closed even if CurrentWave reaches 99.
        /// </summary>
        public bool IsWaveReached(string? featureId)
        {
            if (string.IsNullOrEmpty(featureId)) return false;
            var wave = WaveOf(featureId);
            return wave < FeatureWaveConstants.WAVE_SENTINEL_UNAVAILABLE && wave <= m_CurrentWave;
        }

        /// <summary>
        /// Future rollout wave below the unavailable sentinel: the feature ships
        /// in this build but is deferred to a later wave. The UI renders a
        /// dimmed preview from DTO defaults; the backend does not register the
        /// systems.
        /// </summary>
        public bool IsPreview(string? featureId) =>
            !string.IsNullOrEmpty(featureId)
            && WaveOf(featureId) < FeatureWaveConstants.WAVE_SENTINEL_UNAVAILABLE
            && !IsWaveReached(featureId);

        /// <summary>
        /// Read-only view of the parsed rollout waves ({featureId -> wave}).
        /// </summary>
        public IReadOnlyDictionary<string, int> Waves => m_Waves;

        /// <summary>
        /// Build a manifest from the raw <see cref="FeatureGatesConfig"/>.
        /// Empty / null config produces an empty manifest (wave 1 / current 1 =
        /// all reached).
        /// </summary>
        public static FeatureManifest FromConfig(FeatureGatesConfig? config)
        {
            var waves = new Dictionary<string, int>(StringComparer.Ordinal);
            if (config?.Waves != null)
            {
                foreach (var kv in config.Waves)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    waves[kv.Key] = kv.Value;
                }
            }

            ValidateWaveOrdering(waves);

            int configuredCurrentWave = config?.CurrentWave ?? 1;
            int currentWave = configuredCurrentWave < 1 ? 1 : configuredCurrentWave;
            if (configuredCurrentWave < 1)
            {
                Log.Warn($"[FEATURE-VERIFY] invalid CurrentWave={configuredCurrentWave}; clamped to 1");
            }

            Log.Info($"manifest built: {waves.Count} wave entries, current wave {currentWave}");
            return new FeatureManifest(waves, currentWave);
        }

        private static void ValidateWaveOrdering(IReadOnlyDictionary<string, int> waves)
        {
            bool hasNetwork = waves.TryGetValue("Network", out var networkWave);
            bool hasArena = waves.TryGetValue("Arena", out var arenaWave);
            bool hasArenaUi = waves.TryGetValue("ArenaUI", out var arenaUiWave);

            if (hasNetwork || hasArena || hasArenaUi)
            {
                if (!hasNetwork || !hasArena || !hasArenaUi)
                    throw new InvalidOperationException(
                        "Invalid feature wave ordering: Network, Arena, and ArenaUI must be configured together.");
            }
            else
            {
                return;
            }

            if (arenaWave < networkWave || arenaUiWave < arenaWave)
            {
                throw new InvalidOperationException(
                    "Invalid feature wave ordering: expected ArenaUI >= Arena >= Network " +
                    $"but got ArenaUI={arenaUiWave}, Arena={arenaWave}, Network={networkWave}.");
            }
        }

        /// <summary>
        /// Build a manifest from the live RemoteBalanceConfig snapshot. Equivalent
        /// to <see cref="FromConfig"/> with the config's <c>FeatureGates</c> section.
        /// </summary>
        public static FeatureManifest FromBalance(RemoteBalanceConfig? balance)
        {
            return FromConfig(balance?.FeatureGates);
        }

        /// <summary>Empty manifest. Every feature defaults to wave 1. Used in tests and as fallback.</summary>
        public static FeatureManifest Empty { get; } =
            new(new Dictionary<string, int>(StringComparer.Ordinal), currentWave: 1);
    }
}
