using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Forecast;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Snapshot of all data needed for wave simulation decisions.
    /// Gathered once per frame, passed to phase transition methods.
    /// </summary>
    public readonly struct WaveSimulationContext
    {
        /// <summary>City SIZE in MW (min 50), sourced from built grid nameplate — NOT live
        /// production. Degradation/damage cuts production but never nameplate, so a city knocked
        /// down by a strike still reads at its true size: waves stay scaled to what was built, and
        /// the readiness gate (not this field) handles the recovery delay. Single "city size"
        /// number shared by calm cadence, threat count, ballistic count, AA magazine and the
        /// Heritage grant — they must agree (capacity↔wave ratio).</summary>
        public readonly int CitySizeMW;

        /// <summary>Enemy pressure multiplier 0.2-1.0 (GridWarfare integration).</summary>
        public readonly float EnemyPressure;

        /// <summary>Season modifier for attack frequency (winter=1.5x, summer=0.8x).</summary>
        public readonly float SeasonModifier;

        /// <summary>Difficulty frequency modifier (affects calm duration).</summary>
        public readonly float FrequencyModifier;

        /// <summary>Difficulty intensity modifier (affects threat count).</summary>
        public readonly float IntensityModifier;

        /// <summary>Whether air attacks are enabled in settings.</summary>
        public readonly bool AttacksEnabled;

        /// <summary>
        /// Degree of built surplus = grid-producer nameplate / (24h peak demand + N+1 unit
        /// buffer). The buffer = min(largest built plant, UnitBufferCapMW) forgives a reserve
        /// of one biggest unit — plants come in build quanta, so a village whose only plant is
        /// a 5 MW windmill (demand 0.5 MW) is not a "spam city". 1 = no surplus (no extra
        /// strikes); &gt; 1 = over-builder that attracts more/larger waves (Фаза 7, путь B).
        /// Floored at 1 in <see cref="WaveContextGatherer.Gather"/> so a power-deficit city
        /// never gets a discount on incoming fire.
        /// </summary>
        public readonly float SurplusRatio;

        /// <summary>
        /// Number of attackable buildings currently in the threat-target cache
        /// (Energy + Critical + Service + Civilian). Scales the wave down when
        /// there is little to hit so an empty or near-empty map is not shelled
        /// into bare ground. -1 means "unknown" (target source not ready) — the
        /// scaling cap is then skipped and behaviour matches the pre-cache path.
        /// </summary>
        public readonly int TargetCount;

        /// <summary>
        /// Distinct intermittent generation types in the built fleet (Wind/Solar, 0..2)
        /// from <c>PowerCapacitySnapshot.IntermittentTypeCount</c>. Each widens the
        /// surplus-free threshold by <c>GenerationSaturation.HeadroomPerType</c> — the same
        /// diversity headroom the degradation axis grants, so "what does not degrade does
        /// not draw fire" holds for any fleet mix.
        /// </summary>
        public readonly int IntermittentTypeCount;

        /// <summary>
        /// Wave-density surcharge signal = defencePotential / DensityRefGuns (population-derived and
        /// crew-aware, see <see cref="Logic.ManpowerLogic.DefensePotential"/>). 0 = no signal
        /// (population unknown / system not initialised) ⇒ NO density surcharge (fail-safe: never
        /// escalate blind). A small starting city ≈ 1; a populous city above DensityFreeThreshold
        /// draws denser waves. Computed in <see cref="WaveContextGatherer.Gather"/>, capped downstream
        /// by DensityMaxSizeMult.
        /// </summary>
        public readonly float DensityRatio;

        /// <summary>
        /// Recovery factor ∈ [0,1] gating the COMBINED size surcharge (surplus × density): how much
        /// escalation the city has earned given its current damage (1 = intact, full surcharge; 0 =
        /// wounded past SurchargeLethalFraction, surcharge collapses to a bare base wave). From
        /// <see cref="Logic.WaveReadinessGate.RecoveryFactor"/> on the snapshot's lostFraction. 0 when
        /// the power snapshot is not ready (fail-safe: do not escalate blind to whether the city is
        /// wounded — escalating a possibly-struck city could finish it off).
        /// </summary>
        public readonly float RecoveryFactor;

        public WaveSimulationContext(
            int citySizeMW,
            float enemyPressure,
            float seasonModifier,
            float frequencyModifier,
            float intensityModifier,
            bool attacksEnabled,
            float surplusRatio,
            int targetCount,
            int intermittentTypeCount = 0,
            float densityRatio = 0f,
            float recoveryFactor = 0f)
        {
            CitySizeMW = citySizeMW;
            EnemyPressure = enemyPressure;
            SeasonModifier = seasonModifier;
            FrequencyModifier = frequencyModifier;
            IntensityModifier = intensityModifier;
            AttacksEnabled = attacksEnabled;
            SurplusRatio = surplusRatio;
            TargetCount = targetCount;
            IntermittentTypeCount = intermittentTypeCount;
            DensityRatio = densityRatio;
            RecoveryFactor = recoveryFactor;
        }

        /// <summary>
        /// Default context for when system is not initialized.
        /// Attacks disabled, no surplus, minimal production.
        /// </summary>
        public static WaveSimulationContext Default => new(
            citySizeMW: WaveContextGatherer.MIN_PRODUCTION_MW,
            enemyPressure: 1f,
            seasonModifier: 1f,
            frequencyModifier: 1f,
            intensityModifier: 1f,
            attacksEnabled: false,
            surplusRatio: 1f,
            targetCount: -1);
    }

    /// <summary>
    /// Gathers wave simulation context from various game systems.
    /// All dependencies passed to <see cref="Gather"/>; the only retained state is
    /// the once-per-session fallback warning flags, reset via
    /// <see cref="ResetStaticState"/> from <c>WaveScheduler.ResetState</c>.
    /// </summary>
    public static class WaveContextGatherer
    {
        private static readonly LogContext Log = new("WaveContextGatherer");
        public const int MIN_PRODUCTION_MW = 50;
        private const float KW_PER_MW = 1000f;

        /// <summary>
        /// Convert a grid capacity reading (kW) to city size in MW, floored at
        /// <see cref="MIN_PRODUCTION_MW"/>. The single boundary conversion shared by every "city
        /// size" consumer (wave cadence/count, ballistic count, AA magazine, Heritage grant) so they
        /// all read the same number (capacity↔wave ratio coherent). Feed it NAMEPLATE (built
        /// capacity), not live production — production is cut by damage/load and would shrink the
        /// city's apparent size after a strike; the readiness gate owns the recovery delay instead.
        /// </summary>
        public static int ToCitySizeMW(int capacityKw)
            => math.max((int)math.round(capacityKw / KW_PER_MW), MIN_PRODUCTION_MW);

        /// <summary>
        /// City SIZE in MW from the power-capacity snapshot's built nameplate, with a graceful
        /// fallback to live <paramref name="productionKw"/> when the snapshot is not ready (boot /
        /// no plants / Engineering not registered yet). The single resolver every "city size"
        /// consumer OUTSIDE the wave context (ballistic count, AA magazine, Heritage grant) uses, so
        /// they read the same nameplate the <see cref="Gather"/> path feeds the calm/threat formulas.
        /// Self-resolves the reader via <see cref="ServiceRegistry"/> (null-tolerant — never throws),
        /// so callers pass only their live production reading; cold path (per wave/placement), not hot.
        /// </summary>
        public static int ResolveCitySizeMW(int productionKw)
        {
            // IPowerCapacitySnapshotReader is AlwaysOpen (Engineering), registered before any
            // consumer — Require, not TryGet (CIVIC463: defensive TryGet would mask an ordering bug).
            // The "snapshot not ready" case is the NameplateKW <= 0 branch below, not a missing reader.
            if (ServiceRegistry.IsInitialized)
            {
                var reader = ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
                if (reader.TryGetSnapshot(out var snap) && snap.NameplateKW > 0)
                    return ToCitySizeMW(snap.NameplateKW);
            }
            return ToCitySizeMW(productionKw); // snapshot not ready / pre-init → live production, floored
        }
        private const float MIN_ENEMY_PRESSURE = 0.2f;
        private const int FALLBACK_SEASON = 0; // Spring — neutral DefaultFrequencyMod
        private static bool s_ClimateFallbackWarned;
        private static bool s_EnemyStateFallbackWarned;

        /// <summary>
        /// Gather simulation context from current game state.
        /// </summary>
        /// <param name="powerGrid">Power grid singleton (default if not available).</param>
        /// <param name="enemyState">Enemy state singleton (default if not available).</param>
        /// <param name="climateAdapter">Climate adapter for temperature (null if not available).</param>
        /// <param name="settings">Mod settings (null if not available).</param>
        /// <param name="nameplateKW">Built grid-producer nameplate Σ (kW) from the Core power
        /// capacity snapshot — base of the surplus signal. Degradation does NOT touch it, so a
        /// spam city cannot hide behind degraded production. 0 → snapshot not ready yet.</param>
        /// <param name="peakDemandKW">24h rolling demand peak (kW) from DemandPeakSingleton.
        /// 0 → falls back to instantaneous <c>powerGrid.Demand</c>.</param>
        /// <param name="largestPlantKW">Largest single built grid-producer nameplate (kW) from
        /// the Core power capacity snapshot — base of the N+1 unit buffer
        /// (min(largest, UnitBufferCapMW) is forgiven before surplus counts). 0 → no buffer
        /// (snapshot not ready / no plants), ratio degrades to the unbuffered form.</param>
        /// <param name="intermittentTypeCount">Distinct intermittent types (Wind/Solar, 0..2)
        /// from the Core power capacity snapshot — widens the surplus-free threshold the same
        /// way the degradation axis widens its headroom. 0 → no diversity bonus.</param>
        /// <param name="dispatchableMW">Live dispatchable capacity (MW, damage/collapse-cut, NOT
        /// load) from the Core power capacity snapshot. With <paramref name="nameplateKW"/> it gives
        /// the city's lostFraction → the recovery gate that shaves the size surcharge on a wounded
        /// city. 0 + nameplate &gt; 0 = fully knocked out; nameplate ≤ 0 = snapshot not ready (the
        /// surcharge then fail-safes off).</param>
        /// <param name="population">City population (vanilla Population singleton). Base of the
        /// defence-potential density signal via <see cref="ManpowerLogic.DefensePotential"/>. 0 →
        /// unknown (boot / no city) → no density surcharge (fail-safe).</param>
        public static WaveSimulationContext Gather(
            PowerGridSingleton powerGrid,
            EnemyState enemyState,
            ClimateState? climateAdapter,
            ModSettings? settings,
            int nameplateKW = 0,
            int peakDemandKW = 0,
            bool powerGridReady = true,
            bool enemyStateReady = true,
            int targetCount = -1,
            int largestPlantKW = 0,
            int intermittentTypeCount = 0,
            float gameTimeHours = 0f,
            int dispatchableMW = 0,
            int population = 0)
        {
            if (!powerGridReady)
                return WaveSimulationContext.Default;

            // City SIZE in MW (min 50) — from built nameplate (NOT live production): damage cuts
            // production but never nameplate, so a struck city keeps its true size and waves stay
            // scaled to what was built. nameplateKW = 0 (snapshot not ready) floors to MIN via
            // ToCitySizeMW, matching the old production-floor behaviour during boot.
            int citySizeMW = ToCitySizeMW(nameplateKW);

            // One config snapshot for the whole gather — used for the enemy-pressure cap
            // here and the surplus formula below (single read avoids a torn hot-reload).
            var balanceConfig = BalanceConfig.Current;

            // optional-neutral: GridWarfare may be closed; missing EnemyState uses neutral pressure.
            float enemyPressure = 1f;
            if (enemyStateReady)
            {
                // Wave feedback now reads the aggregate of the enemy's three axes
                // (mean / cap) — the single seam where the enemy's axis health scales
                // incoming waves, replacing the former single Pressure field.
                enemyPressure = enemyState.AggregatePressure01(balanceConfig.GridWarfare.PressureCap);

                // Respite (Phase 3.6.3): each axis the player floored is in a "regroup" window;
                // waves weaken by one RespiteWaveWeakenMultiplier per active window. This is the
                // single seam where the suppression loop feeds back into wave strength — applied
                // here (not on the axis itself) so the durable axis value and its regen are
                // untouched and only the live wave force reflects the temporary lull.
                int respiteAxes = enemyState.RespiteActiveAxisCount(gameTimeHours);
                if (respiteAxes > 0)
                {
                    float weaken = math.clamp(balanceConfig.GridWarfare.RespiteWaveWeakenMultiplier, 0f, 1f);
                    enemyPressure *= math.pow(weaken, (float)respiteAxes);
                }

                enemyPressure = math.clamp(enemyPressure, MIN_ENEMY_PRESSURE, 1f);
            }
            else
                WarnEnemyStateFallback();

            // Season modifier from vanilla currentSeasonName (calendar-based, matches game UI)
            int season;
            if (climateAdapter == null)
            {
                WarnClimateFallbackOnce();
                season = FALLBACK_SEASON;
            }
            else
            {
                season = climateAdapter.Current.Season;
            }
            float seasonModifier = WaveScalingService.GetSeasonModifier(season);

            // Attack modifiers from difficulty preset
            var (frequencyMod, intensityMod) = GetAttackModifiers(settings);

            // Attacks enabled?
            bool attacksEnabled = settings != null && settings.AirAttacks != AirAttackPreset.Off;

            // Surplus degree (Фаза 7, путь B).
            // INVARIANT: surplus base is NAMEPLATE (built capacity), NOT Production (actual output).
            // degradation cuts Production but never Nameplate, so a spam city cannot hide its
            // over-build behind the degradation that ate its effective output. Compared against
            // the 24h demand peak PLUS the N+1 unit buffer: a reserve of one biggest built unit
            // (capped by UnitBufferCapMW) is forgiven — plants come in build quanta, so a small
            // city's single available plant must not read as over-build and pull the surcharge
            // ceiling onto a player with no air defense yet. Mirrors the degradation axis
            // (SaturationLogic.ComputeTargetFactor). Floored at 1 — a deficit city
            // (nameplate < peak) gets NO discount on incoming fire, only over-builders get the
            // surcharge.
            int peakKW = peakDemandKW > 0 ? peakDemandKW : powerGrid.Demand;
            int nameplateMW = (int)math.round(nameplateKW / KW_PER_MW);
            // The surplus formula is owned by PowerForecast.SurplusRatio (single source — the
            // forecast and this runtime gatherer must agree, see H2 WAV-1). The N+1 unit buffer
            // input diverges DELIBERATELY by data source: this runtime path forgives the LARGEST
            // built plant (largestPlantKW), the forecast forgives its plantMW (mean in archetype,
            // largest in live mode). We pass largestPlantMW as the plantMW argument so the buffer
            // = min(largestPlantMW, UnitBufferCapMW) — byte-identical to the old inline buffer.
            // OFFSET: PowerForecast.SurplusRatio floors the denominator at 0.001f, the old inline
            // floored it at 1f. For any city above the 50 MW production min the peak-demand term
            // alone is several MW, so the floor never binds — the two agree on every realistic
            // input; they part only on a degenerate near-zero-demand, near-zero-buffer city.
            float surplusRatio = PowerForecast.SurplusRatio(
                balanceConfig,
                nameplate: nameplateMW,
                peakDemand: peakKW / KW_PER_MW,
                plantMW: largestPlantKW / KW_PER_MW);

            // Wave-density surcharge (population → defence potential) + its recovery gate. Both reuse
            // EXISTING Core formulas (variant D — no new arithmetic here): the density signal calls
            // ManpowerLogic, the recovery factor calls WaveReadinessGate on the snapshot's damage
            // reading — so runtime, forecast and server recompute can never drift from one copy.
            // FAIL-SAFE direction (opposite of the frequency gate): missing data ⇒ NO surcharge.
            //   • population 0 → defencePotential 0 → densityRatio 0 → no density surcharge.
            //   • power snapshot not ready (nameplateMW ≤ 0) → recoveryFactor 0 → the WHOLE size
            //     surcharge (surplus + density) collapses to 1 — escalating a city whose damage we
            //     cannot read could finish off a struck one, so by default we do not escalate.
            var wavesCfg = balanceConfig.Waves;
            float densityRatio = 0f;
            if (population > 0)
            {
                float defencePotential = ManpowerLogic.DefensePotential(
                    population, balanceConfig.AAUnits.HeritageCrewRequired);
                float refGuns = math.max(wavesCfg.DensityRefGuns, 1f); // floor: never divide by ≤ 0
                densityRatio = defencePotential / refGuns;
            }
            float recoveryFactor = 0f;
            if (nameplateMW > 0)
            {
                float lostFraction = WaveReadinessGate.ComputeLostFraction(dispatchableMW, nameplateMW);
                recoveryFactor = WaveReadinessGate.RecoveryFactor(lostFraction, wavesCfg.SurchargeLethalFraction);
            }

            return new WaveSimulationContext(
                citySizeMW,
                enemyPressure,
                seasonModifier,
                frequencyMod,
                intensityMod,
                attacksEnabled,
                surplusRatio,
                targetCount,
                math.max(0, intermittentTypeCount),
                densityRatio,
                recoveryFactor);
        }

        /// <summary>
        /// Get frequency and intensity modifiers for air attack preset.
        /// </summary>
        private static (float frequency, float intensity) GetAttackModifiers(ModSettings? settings)
        {
            if (settings == null)
                return (1f, 1f);

            var waves = BalanceConfig.Current.Waves;

            // Frequency half delegates to the single-source map (H2 WAV-2). Runtime keeps its
            // DELIBERATE preset edges: Off → 0f (attacks truly disabled), unknown → NormalFrequency.
            float frequency = WaveScalingService.GetFrequencyMod(
                settings.AirAttacks, waves, offMod: 0f, defaultMod: waves.NormalFrequency);

            // Intensity half stays inline — it is not duplicated in the forecast (WAV-2 is the
            // frequency-only dup); only frequency has a second consumer.
            float intensity = settings.AirAttacks switch
            {
                AirAttackPreset.Off => 0f,
                AirAttackPreset.Light => waves.LightIntensity,
                AirAttackPreset.Normal => waves.NormalIntensity,
                AirAttackPreset.Heavy => waves.HeavyIntensity,
                AirAttackPreset.Overwhelming => waves.OverwhelmingIntensity,
                _ => waves.NormalIntensity
            };

            return (frequency, intensity);
        }

        private static void WarnClimateFallbackOnce()
        {
            if (s_ClimateFallbackWarned)
                return;

            s_ClimateFallbackWarned = true;
            Log.Warn("ClimateState unavailable or invalid; wave context using neutral 15C fallback");
        }

        private static void WarnEnemyStateFallback()
        {
            if (s_EnemyStateFallbackWarned) return;

            // During boot the feature-state decision is racy (manifest may still
            // be populating). Skip — a subsequent call after
            // IsRegistrationComplete will classify correctly.
            if (!FeatureRegistry.IsInitialized
                || !FeatureRegistry.Instance.IsRegistrationComplete)
            {
                return;
            }

            if (FeatureRegistry.Instance.IsUnavailable(FeatureIds.GridWarfareName, out var reason))
            {
                if (reason == FeatureUnavailableReason.Closed
                    || reason == FeatureUnavailableReason.WaveLocked
                    || reason == FeatureUnavailableReason.DependencySkipped)
                {
                    // Documented optional-neutral path — GridWarfare is closed
                    // / preview / dep-skipped, EnemyState absent BY DESIGN.
                    // Debug-only signal so the line is recoverable but not
                    // noise in production log.
                    // CRITICAL: do NOT set s_EnemyStateFallbackWarned here.
                    // Flagging on the design-path would silence a later real
                    // producer-bug Warn (e.g. cross-fixture lifecycle where
                    // manifest mutates between calls, or hot-reload changing
                    // registration state). The one-shot guard is meaningful
                    // only for the actual warn-pathes.
                    if (Log.IsDebugEnabled)
                        Log.Debug($"EnemyState absent (GridWarfare {reason}); wave context using neutral pressure");
                    return;
                }

                // Failed: not an expected gated state. Feature registry could
                // not make GridWarfare available — neutral pressure is a
                // degraded-mode fallback, not a normal optional path. Warn once.
                s_EnemyStateFallbackWarned = true;
                Log.Warn($"EnemyState unavailable because GridWarfare is {reason}; wave context using neutral pressure");
                return;
            }

            // GridWarfare reports open & registered, but the singleton is
            // missing — real producer wiring bug (EnemySimulationSystem failed
            // to EnsureExists, race with destroy, registration order broken).
            // Warn once.
            s_EnemyStateFallbackWarned = true;
            Log.Warn("EnemyState unavailable although GridWarfare feature is open; wave context using neutral pressure");
        }

        /// <summary>
        /// Reset static once-warn flags. Called from <c>WaveScheduler.ResetState()</c>
        /// so each new-game/load session gets a clean diagnostic surface and the
        /// fallback warning is not silenced forever by a previous session.
        /// </summary>
        public static void ResetStaticState()
        {
            s_ClimateFallbackWarned = false;
            s_EnemyStateFallbackWarned = false;
        }
    }
}
