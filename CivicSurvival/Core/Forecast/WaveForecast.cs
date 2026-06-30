using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer wave-scheduling model: wraps <see cref="WaveScalingService"/> with the sweep's
    /// fixed-scenario assumptions (neutral season, no random variance, no intermittent generation),
    /// so the crisis sweep schedules the next attack and prices the calm window from one place. NOT
    /// new balance arithmetic — the season/variance/intermittent conventions are the only thing
    /// bundled here; the durations come straight from WaveScalingService, the same path the runtime
    /// wave scheduler uses.
    /// </summary>
    public static class WaveForecast
    {
        /// <summary>Phase (Alert / Attack / Recovery) duration at the wave number, frequency mod and
        /// the given random midpoint (0.5 = the Min+Max midpoint the deterministic modes read).</summary>
        public static float PhaseDuration(GamePhase phase, int waveNum, float freqMod, float randomValue)
            => WaveScalingService.GetPhaseDuration(phase, waveNum, freqMod, randomValue);

        /// <summary>Calm-window seconds for the given generation MW, under the sweep's fixed scenario:
        /// neutral season (GetSeasonModifier(0)), full random variance (1.0) and no intermittent
        /// generation. The surplus surcharge (over-build penalty) is the only city-shaped input.</summary>
        public static float CalmSeconds(int mwNameplate, float freqMod, float surplusRatio)
            => CalmSeconds(mwNameplate, WaveScalingService.GetSeasonModifier(0), freqMod, surplusRatio);

        /// <summary>Calm-window seconds with an explicit season modifier (Tier-0 live path): the
        /// caller passes the live season's frequency mod (<see cref="WaveScalingService.GetSeasonModifier"/>)
        /// instead of the fixed neutral one. Full random variance (1.0) and no intermittent
        /// generation are still the sweep's fixed-scenario conventions; the surplus surcharge is the
        /// only city-shaped input. The neutral-season overload above forwards here with
        /// <c>GetSeasonModifier(0)</c>, so archetype-fallback output is byte-identical.</summary>
        public static float CalmSeconds(int mwNameplate, float seasonModifier, float freqMod, float surplusRatio)
            => WaveScalingService.CalculateCalmSeconds(
                mwNameplate, seasonModifier, freqMod,
                randomVariance: 1.0f, surplusRatio: surplusRatio, intermittentTypes: 0);

        /// <summary>Live wave-frequency modifier for the player's air-attack difficulty preset
        /// (Tier-0 live path) — the same preset→frequency mapping the runtime
        /// <c>WaveContextGatherer.GetAttackModifiers</c> uses. Returned ONLY when the sweep runs in
        /// live mode; the archetype-fallback path keeps <c>cfg.Waves.DefaultFrequencyMod</c> (the
        /// deliberate fixed-scenario assumption), so the fallback verdict never reads player settings.</summary>
        public static float LiveFrequencyMod(WavesConfig waves, AirAttackPreset preset)
            // Delegates to the single-source preset→frequency map (H2 WAV-2). The forecast keeps its
            // DELIBERATE edge: Off → DefaultFrequencyMod (the sweep prices the default scenario, it
            // does not model the "no attacks" toggle) — same value as the unknown fallback, unlike
            // the runtime gatherer which distinguishes Off (0f) from unknown (NormalFrequency).
            => WaveScalingService.GetFrequencyMod(
                preset, waves, offMod: waves.DefaultFrequencyMod, defaultMod: waves.DefaultFrequencyMod);
    }
}
