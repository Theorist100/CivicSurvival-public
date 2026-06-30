using ArgumentException = System.ArgumentException;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Static helper for wave scaling calculations.
    /// All randomness passed as parameters for testability.
    ///
    /// Scaling formulas:
    /// - Calm duration: BASE / log2(MW/100 + 1) * mods
    /// - Massive chance: 15% + 2%/wave (max 40%)
    /// - Threat count: (sizeFactor + timeFactor) * noise * intensityMod
    ///
    /// Note: GetTargetingRatios and EstimateBallisticCount delegate to WaveHelper (Core)
    /// to avoid duplication for cross-domain consumers.
    /// </summary>
    public static class WaveScalingService
    {
        private static readonly LogContext Log = new("WaveScaling");

        // ===== Scaling Constants =====
        private const int MIN_PRODUCTION_MW = 50;
        private const float SECONDS_PER_MINUTE = 60f;
        private const float NOISE_MIN = 0.8f;
        private const float NOISE_MAX = 1.2f;

        // ===== Season indices (mirror VanillaClimateAdapter.GetSeason) =====
        private const int SEASON_SUMMER = 1;
        private const int SEASON_WINTER = 3;

        // ===== Misc =====
        private const float DEFAULT_PHASE_SECONDS = 600f;

        // ============================================================================
        // Surplus-attracts-strikes (Фаза 7, путь B)
        // ============================================================================

        /// <summary>
        /// Built-surplus multipliers for wave frequency and size. <paramref name="surplusRatio"/>
        /// is nameplate / (24h peak demand + N+1 unit buffer) (≥ 1, see
        /// <see cref="WaveSimulationContext.SurplusRatio"/>). Nothing above the free threshold
        /// ⇒ no surcharge (an honest reserve up to SurplusFreeThreshold draws no extra fire).
        /// Above it, frequency and size each grow linearly by their gain, capped by
        /// SurplusMax*Mult. Both multipliers ∈ [1, max] — surcharge only goes up.
        /// </summary>
        private static (float freqMult, float sizeMult) SurplusMultipliers(float surplusRatio, int intermittentTypes, WavesConfig cfg)
        {
            if (!cfg.SurplusStrikesEnabled)
                return (1f, 1f);

            // Diversity headroom — the SAME bonus the degradation axis grants
            // (GenerationSaturation.HeadroomPerType per intermittent Wind/Solar type, shared
            // key on purpose): a reserve held against tonight's calm or the daily night is
            // honest, so it must not draw fire either. 0..2 types ⇒ natural ceiling, no clamp.
            float freeThreshold = cfg.SurplusFreeThreshold
                + BalanceConfig.Current.GenerationSaturation.HeadroomPerType * math.max(0, intermittentTypes);
            float over = math.max(0f, surplusRatio - freeThreshold);
            float freqMult = math.min(1f + cfg.SurplusFrequencyGain * over, cfg.SurplusMaxFreqMult);
            float sizeMult = math.min(1f + cfg.SurplusSizeGain * over, cfg.SurplusMaxSizeMult);
            return (freqMult, sizeMult);
        }

        /// <summary>
        /// Wave-density surcharge from the city's DEFENCE POTENTIAL (population-derived, see
        /// <see cref="Logic.ManpowerLogic.DefensePotential"/>). Mirror of the surplus-power surcharge:
        /// a city that COULD field a strong defence (high population → many crewable guns) draws
        /// denser waves so its defence does not trivialise the threat. <paramref name="densityRatio"/>
        /// = defencePotential / DensityRefGuns (a small starting city ≈ 1). Nothing above
        /// DensityFreeThreshold ⇒ no surcharge (early/small cities untouched); above it size grows
        /// linearly by DensitySizeGain, capped at DensityMaxSizeMult. Result ∈ [1, cap] — the
        /// surcharge only goes up. Disabled, or an unknown population (ratio ≤ 0 = the fail-safe
        /// "no signal"), returns 1.
        /// </summary>
        public static float DensityMultiplier(float densityRatio, WavesConfig cfg)
        {
            if (!cfg.DensityStrikesEnabled)
                return 1f;
            if (!math.isfinite(densityRatio) || densityRatio <= 0f)
                return 1f;

            float over = math.max(0f, densityRatio - cfg.DensityFreeThreshold);
            return math.min(1f + cfg.DensitySizeGain * over, cfg.DensityMaxSizeMult);
        }

        /// <summary>
        /// Single recovery gate over the COMBINED size surcharge (surplus × density). Shaves the
        /// EXCESS over 1 by <paramref name="recoveryFactor"/> (∈ [0,1], from
        /// <see cref="Logic.WaveReadinessGate.RecoveryFactor"/>) so a wounded city — whatever raised
        /// its surcharge — is never escalated into a death spiral: recoveryFactor 1 (intact) applies
        /// the surcharge fully, 0 (damaged past SurchargeLethalFraction) collapses it to a bare base
        /// wave. ONE helper for BOTH axes (DRY): a struck over-builder must not be finished off by the
        /// surplus surcharge any more than the density one. NaN/Inf inputs degrade to "no surcharge".
        /// </summary>
        public static float ApplySurchargeGuards(float sizeMultRaw, float recoveryFactor)
        {
            if (!math.isfinite(sizeMultRaw) || sizeMultRaw < 1f)
                sizeMultRaw = 1f;
            float factor = math.isfinite(recoveryFactor) ? math.clamp(recoveryFactor, 0f, 1f) : 0f;
            return 1f + (sizeMultRaw - 1f) * factor;
        }

        // ============================================================================
        // Calm Duration
        // ============================================================================

        /// <summary>
        /// Inter-wave calm duration (the lull BETWEEN waves) using simulation context. Uses the
        /// shared base tempo <c>Waves.FrequencyBaseMinutes</c>.
        /// Logs internally for diagnostics.
        /// </summary>
        public static float CalculateCalmDuration(in WaveSimulationContext ctx, ref Random random)
            => CalculateCalmDurationWithBase(ctx, ref random, BalanceConfig.Current.Waves.FrequencyBaseMinutes);

        /// <summary>
        /// FIRST calm duration after war start (the wait before the OPENING attack). Uses its own
        /// base tempo <c>Waves.FirstCalmBaseMinutes</c> so the first strike can be brought closer
        /// without also shortening the inter-wave pacing.
        /// </summary>
        public static float CalculateFirstCalmDuration(in WaveSimulationContext ctx, ref Random random)
            => CalculateCalmDurationWithBase(ctx, ref random, BalanceConfig.Current.Waves.FirstCalmBaseMinutes);

        private static float CalculateCalmDurationWithBase(in WaveSimulationContext ctx, ref Random random, float baseMinutes)
        {
            EnsureSeeded(in random, nameof(CalculateCalmDuration));

            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            float randomVariance = random.NextFloat(
                cfg.FrequencyRngMin,
                cfg.FrequencyRngMax);

            float calmSeconds = CalculateCalmSeconds(
                ctx.CitySizeMW,
                ctx.SeasonModifier,
                ctx.FrequencyModifier,
                randomVariance,
                ctx.SurplusRatio,
                ctx.IntermittentTypeCount,
                baseMinutesOverride: baseMinutes);

            if (Log.IsDebugEnabled) Log.Debug($" CALM baseTempo: {ctx.CitySizeMW}MW(nameplate), base={baseMinutes:F0}min, season={ctx.SeasonModifier:F1}x, freq={ctx.FrequencyModifier:F1}x, surplus={ctx.SurplusRatio:F2}x -> {calmSeconds / 60f:F1}min");

            return calmSeconds;
        }

        /// <summary>
        /// Base calm cadence (the MINIMUM inter-wave interval) by city SIZE. The dynamic readiness
        /// gate (<see cref="Logic.WaveReadinessGate"/>) may hold the wave longer while the city
        /// recovers, but never launches sooner than this. citySizeMW is NAMEPLATE (built capacity),
        /// not live production — a struck city keeps its size, the gate handles the recovery delay.
        /// Result (undamaged, raw before the MinCalmSeconds floor): 100MW≈110min, 500MW≈43min,
        /// 2000MW≈25min — the ~45-min MinCalmSeconds floor binds for most sizes above ~100MW.
        /// </summary>
        public static float CalculateCalmSeconds(int citySizeMW, float seasonMod, float frequencyMod, float randomVariance, float surplusRatio = 1f, int intermittentTypes = 0, float baseMinutesOverride = -1f)
        {
            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            citySizeMW = math.max(citySizeMW, MIN_PRODUCTION_MW);

            // Guard against misconfigured balance constants (T-023)
            float frequencyDivisor = math.max(cfg.FrequencyMwDivisor, 1f);

            // Logarithmic scaling: bigger city = shorter calm
            float logFactor = math.log2(citySizeMW / frequencyDivisor + 1f);
            logFactor = math.max(logFactor, 0.5f);

            // baseMinutesOverride < 0 ⇒ use the shared inter-wave tempo; the first-calm path passes
            // Waves.FirstCalmBaseMinutes so the opening wait is tuned independently.
            float baseMinutesEffective = baseMinutesOverride >= 0f ? baseMinutesOverride : cfg.FrequencyBaseMinutes;
            float baseCalmMinutes = baseMinutesEffective / logFactor;

            // Apply modifiers
#pragma warning disable CIVIC247 // Floor applied on line below (math.max with MinCalmSeconds)
            float calmMinutes = baseCalmMinutes * seasonMod * frequencyMod * randomVariance;
#pragma warning restore CIVIC247

            // Built-surplus surcharge (Фаза 7): more over-build = higher frequency = shorter calm.
            var (freqMult, _) = SurplusMultipliers(surplusRatio, intermittentTypes, cfg);
            calmMinutes /= freqMult;

            // Convert to seconds, enforce minimum
            float calmSeconds = calmMinutes * SECONDS_PER_MINUTE;
            calmSeconds = math.max(calmSeconds, cfg.MinCalmSeconds);

            return calmSeconds;
        }

        // ============================================================================
        // Wave Type Determination
        // ============================================================================

        /// <summary>
        /// Determine wave type using Random ref.
        /// Logs internally for diagnostics.
        /// </summary>
        public static WaveType DetermineWaveType(int waveNumber, ref Random random)
        {
            EnsureSeeded(in random, nameof(DetermineWaveType));

            float randomValue = random.NextFloat();
            var waveType = DetermineWaveType(waveNumber, randomValue);

            if (Log.IsDebugEnabled) Log.Debug($" Wave type: wave #{waveNumber} -> {waveType}");

            return waveType;
        }

        /// <summary>
        /// Determine wave type based on random roll.
        /// Chance of MassiveStrike: 15% + 2%/wave (max 40%)
        /// </summary>
        public static WaveType DetermineWaveType(int waveNumber, float randomValue)
        {
            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            float massiveChance = cfg.MassiveStrikeBaseChance
                                + (waveNumber * cfg.MassiveStrikeWaveBonus);
            massiveChance = math.min(massiveChance, cfg.MassiveStrikeMaxChance);

            return randomValue < massiveChance ? WaveType.MassiveStrike : WaveType.Harassment;
        }

        // ============================================================================
        // Threat Count Calculation
        // ============================================================================

        /// <summary>
        /// Calculate threat count using simulation context.
        /// Logs internally for diagnostics.
        /// </summary>
        public static int CalculateThreatCount(in WaveSimulationContext ctx, WaveType waveType, int waveNumber, ref Random random, float strengthMult = 1f)
        {
            EnsureSeeded(in random, nameof(CalculateThreatCount));

            // ±20% multiplicative noise (design doc: noise around calculated base)
            float noiseMult = random.NextFloat(NOISE_MIN, NOISE_MAX);

            int threats = CalculateThreatCount(
                waveType,
                waveNumber,
                ctx.CitySizeMW,
                ctx.IntensityModifier,
                noiseMult,
                ctx.EnemyPressure,
                ctx.TargetCount,
                ctx.SurplusRatio,
                ctx.IntermittentTypeCount,
                ctx.DensityRatio,
                ctx.RecoveryFactor,
                strengthMult);

            // ThreatScaleDiag (Debug): why a given MW yields a given count — shows whether the count is
            // inflated by the surplus surcharge (size > 1 on a deficit city = bug) or the density
            // surcharge (population), and whether the recovery gate shaved it on a wounded city
            // (recovery < 1 ⇒ sizeMult pulled back toward 1). Per-wave only (phase transition, not
            // hot-path). Enable Debug for [WaveScaling] to see it.
            if (Log.IsDebugEnabled)
            {
                var diagW = BalanceConfig.Current.Waves;
                var (_, diagSurplusMult) = SurplusMultipliers(ctx.SurplusRatio, ctx.IntermittentTypeCount, diagW);
                float diagDensityMult = DensityMultiplier(ctx.DensityRatio, diagW);
                float diagSizeMult = ApplySurchargeGuards(diagSurplusMult * diagDensityMult, ctx.RecoveryFactor);
                Log.Debug($"[ThreatScaleDiag] {waveType} #{waveNumber}: {ctx.CitySizeMW}MW(nameplate) intensity={ctx.IntensityModifier:F2}x pressure={ctx.EnemyPressure:P0} surplus={ctx.SurplusRatio:F2}x(size={diagSurplusMult:F2}x) density={ctx.DensityRatio:F2}x(size={diagDensityMult:F2}x) recovery={ctx.RecoveryFactor:F2} sizeMult={diagSizeMult:F2}x targets={ctx.TargetCount} -> {threats} | cfg SizeMult={diagW.SizeFactorMult} MassiveMult={diagW.MassiveSizeMult} Max={diagW.MaxThreats}");
            }

            return threats;
        }

        /// <summary>
        /// Calculate threat count for wave.
        /// Harassment: sizeFactor + timeFactor (design doc formula).
        /// Massive: amplified scaling with wave escalation.
        /// noiseMult: multiplicative noise [0.8, 1.2] around calculated base.
        /// strengthMult: flat scale on the computed base (1 = unchanged). The intro wave uses the
        /// regular Harassment formula with this set to Waves.IntroStrengthMult so the first strike
        /// is a capped uplift over a normal wave rather than a forced MassiveStrike.
        /// </summary>
        public static int CalculateThreatCount(WaveType waveType, int waveNumber, int citySizeMW, float intensityMod, float noiseMult, float enemyPressure = 1f, int targetCount = -1, float surplusRatio = 1f, int intermittentTypes = 0, float densityRatio = 0f, float recoveryFactor = 1f, float strengthMult = 1f)
        {
            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            // Nothing targetable (known-empty cache) — no wave at all. Returns before the
            // MinThreats floor so an empty map is not shelled with drones aimed at bare ground.
            // targetCount < 0 means "unknown" (source not ready) — fall through unchanged.
            if (targetCount == 0)
                return 0;

            citySizeMW = math.max(citySizeMW, MIN_PRODUCTION_MW);

            // Guard against misconfigured balance constants (T-023)
            float sizeFactorDiv = math.max(cfg.SizeFactorDiv, 1f);

            // Size factor: logarithmic growth
            float sizeFactor = math.log2(citySizeMW / sizeFactorDiv + 1f)
                             * cfg.SizeFactorMult;

            // Time factor: linear escalation
            float timeFactor = waveNumber * cfg.TimeFactorPerWave;

            float baseThreats;
            if (waveType == WaveType.MassiveStrike)
            {
                // Massive: amplified size scaling + wave escalation
                baseThreats = sizeFactor * cfg.MassiveSizeMult + timeFactor * cfg.MassiveTimeMult;
            }
            else
            {
                // Harassment: design doc formula (base = sizeFactor + timeFactor)
                baseThreats = sizeFactor + timeFactor;
            }

            // Multiplicative noise (±20%) instead of additive random base
            baseThreats *= noiseMult;

            // Flat strength scale (intro wave uplift; 1 for every regular wave). Applied with the
            // noise/surplus scales before the single clamp so MaxThreats still caps the result.
            baseThreats *= strengthMult;

            // Combined size surcharge: built-power surplus (Фаза 7, over-build) × defence-potential
            // density (population — a city that COULD defend draws denser waves), both ≥ 1, then the
            // single recovery gate shaves the excess on a damaged city so NEITHER axis finishes off
            // the wounded (no-spiral). Applied to the base BEFORE intensity/pressure and the single
            // MinThreats/MaxThreats clamp, so the MaxThreats ceiling still caps a runaway storm.
            var (_, surplusSizeMult) = SurplusMultipliers(surplusRatio, intermittentTypes, cfg);
            float densitySizeMult = DensityMultiplier(densityRatio, cfg);
            float sizeMult = ApplySurchargeGuards(surplusSizeMult * densitySizeMult, recoveryFactor);
            baseThreats *= sizeMult;

            if (!math.isfinite(intensityMod) || intensityMod <= 0f)
                return 0;

            enemyPressure = math.max(enemyPressure, 0f);
            if (enemyPressure <= 0f)
                return 0;

            // Apply intensity and pressure before the single clamp.
            int scaledThreats = (int)math.round(baseThreats * intensityMod * enemyPressure);

            if (scaledThreats <= 0)
                return 0;

            // Clamp positive waves to valid range. With at least one target the full wave
            // stands: drones scatter across categories, miss via CEP and get intercepted by
            // air defense, and damage accumulates per-hit (capacity / HitCount), so a large
            // wave on a small map is the intended air-defense pressure, not overkill. The
            // only special case is the empty map, handled by the targetCount == 0 early-out.
            int finalThreats = math.clamp(scaledThreats, cfg.MinThreats, cfg.MaxThreats);

            // DIAG (Debug): the WHOLE wave-size math in one line — base = sizeFactor + timeFactor,
            // then ×noise×strength, then the surcharge (surplus × density, gated by recovery →
            // sizeMult), then ×intensity×pressure → pre-clamp → clamp[Min,Max]. Per-wave, not
            // hot-path. Enable Debug for [WaveScaling] to see it.
            if (Log.IsDebugEnabled)
                Log.Debug($"[ThreatMath] {waveType} #{waveNumber}: city={citySizeMW}MW base(size={sizeFactor:F1}+time={timeFactor:F1})={sizeFactor + timeFactor:F1} ×noise={noiseMult:F2}×strength={strengthMult:F2} | surplus={surplusSizeMult:F2}×density={densitySizeMult:F2}@recovery={recoveryFactor:F2}→sizeMult={sizeMult:F2} | ×intensity={intensityMod:F2}×pressure={enemyPressure:F2} = preClamp={scaledThreats} clamp[{cfg.MinThreats},{cfg.MaxThreats}] → {finalThreats}");
            return finalThreats;
        }

        // ============================================================================
        // Utility Methods
        // ============================================================================

        /// <summary>
        /// Single-source map from the player's air-attack difficulty preset to its wave-frequency
        /// modifier (H2 WAV-2). The four "attack-on" presets (Light/Normal/Heavy/Overwhelming) are
        /// owned here so the runtime gatherer (<c>WaveContextGatherer.GetAttackModifiers</c>) and the
        /// forecast (<c>WaveForecast.LiveFrequencyMod</c>) read one table instead of two switches.
        ///
        /// The <c>Off</c> and the unknown-enum fallback are NOT shared — each caller passes its own,
        /// preserving a DELIBERATE divergence: the runtime maps <c>Off → 0f</c> (no attacks at all)
        /// while the forecast maps <c>Off → DefaultFrequencyMod</c> (the sweep does not model the
        /// "no-attack" toggle, it prices the default scenario). The unknown fallback is likewise the
        /// caller's call (runtime → NormalFrequency, forecast → DefaultFrequencyMod).
        /// </summary>
        /// <param name="preset">Player's air-attack difficulty preset.</param>
        /// <param name="waves">Wave config holding the per-preset frequency constants.</param>
        /// <param name="offMod">Value returned for <see cref="AirAttackPreset.Off"/>.</param>
        /// <param name="defaultMod">Value returned for any unknown/future enum member.</param>
        public static float GetFrequencyMod(AirAttackPreset preset, WavesConfig waves, float offMod, float defaultMod)
            => preset switch
            {
                AirAttackPreset.Off => offMod,
                AirAttackPreset.Light => waves.LightFrequency,
                AirAttackPreset.Normal => waves.NormalFrequency,
                AirAttackPreset.Heavy => waves.HeavyFrequency,
                AirAttackPreset.Overwhelming => waves.OverwhelmingFrequency,
                _ => defaultMod,
            };

        /// <summary>
        /// Get season modifier from vanilla season index (0=Spring, 1=Summer, 2=Fall, 3=Winter).
        /// Single source of truth = vanilla currentSeasonName, mapped in VanillaClimateAdapter.
        /// </summary>
        public static float GetSeasonModifier(int season)
        {
            var cfg = BalanceConfig.Current.Waves;
            return season switch
            {
                SEASON_WINTER => cfg.WinterFrequencyMod,
                SEASON_SUMMER => cfg.SummerFrequencyMod,
                _ => cfg.DefaultFrequencyMod,
            };
        }

        /// <summary>
        /// Check if Double Tap should trigger (storm mechanic).
        /// 15% chance after each wave to have next wave in 5-10 min.
        /// Returns short cooldown if triggered, otherwise -1.
        /// </summary>
        public static float CheckDoubleTap(ref Random random)
        {
            EnsureSeeded(in random, nameof(CheckDoubleTap));

            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            if (random.NextFloat() < cfg.DoubleTapChance)
            {
                // Double Tap triggered! Return 5-10 min cooldown
                float range = cfg.DoubleTapMaxSeconds - cfg.DoubleTapMinSeconds;
                return cfg.DoubleTapMinSeconds + (range * random.NextFloat());
            }

            return -1f; // No double tap
        }

        /// <summary>
        /// Get duration for a phase.
        /// Calm: applies frequencyMod and escalation (shorter each wave).
        /// Others: fixed range, no modifiers.
        /// </summary>
        public static float GetPhaseDuration(GamePhase phase, int waveNumber, float frequencyMod, float randomValue)
        {
            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Waves;

            // randomValue is 0-1, used to interpolate between min and max
            switch (phase)
            {
                case GamePhase.Calm:
                    // Escalation: Calm gets shorter as waves progress
                    float waveModifier = math.max(cfg.MinCalmModifier,
                                                   1f - (waveNumber * cfg.EscalationPerWave));
                    float calmMod = frequencyMod * waveModifier;
                    float calmBase = cfg.CalmMin + math.max(0f, cfg.CalmMax - cfg.CalmMin) * randomValue;
                    return math.max(calmBase * calmMod, cfg.MinCalmSeconds);

                case GamePhase.Alert:
                    return cfg.AlertMin + math.max(0f, cfg.AlertMax - cfg.AlertMin) * randomValue;

                case GamePhase.Attack:
                    return cfg.AttackMin + math.max(0f, cfg.AttackMax - cfg.AttackMin) * randomValue;

                case GamePhase.Recovery:
                    return cfg.RecoveryMin + math.max(0f, cfg.RecoveryMax - cfg.RecoveryMin) * randomValue;

                default:
                    return DEFAULT_PHASE_SECONDS;
            }
        }

        /// <summary>
        /// Get targeting ratios for wave type.
        /// Delegates to WaveHelper (Core) for cross-domain consumers.
        /// </summary>
        public static (float energy, float critical, float service, float civilian) GetTargetingRatios(WaveType waveType)
            => WaveHelper.GetTargetingRatios(waveType);

        /// <summary>
        /// Estimate ballistic missile count based on production capacity and wave progression.
        /// Delegates to WaveHelper (Core) for cross-domain consumers.
        /// </summary>
        public static int EstimateBallisticCount(int productionMW, int waveNumber)
            => WaveHelper.EstimateBallisticCount(productionMW, waveNumber);

        private static void EnsureSeeded(in Random random, string caller)
        {
            if (random.state == 0)
                throw new ArgumentException($"{caller}: Random must be seeded before use");
        }
    }
}
