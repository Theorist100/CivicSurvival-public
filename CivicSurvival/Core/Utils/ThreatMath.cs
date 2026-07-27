using System;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Pure math functions for threat calculations.
    /// Stateless, all randomness passed as parameters for testability.
    ///
    /// Moved to Core to avoid cross-domain imports.
    /// </summary>
    public static class ThreatMath
    {
        private const float DEFAULT_TARGET_RATIO = 0.25f;

        /// <summary>
        /// Calculate number of ballistic missiles based on city size and wave number.
        /// Ballistics only appear for cities with 300+ MW production.
        ///
        /// Formula: base(MW/500) + waveBonus(0.5 per wave after start)
        /// </summary>
        /// <returns>Number of ballistics to spawn (0 if below threshold)</returns>
        public static int CalculateBallisticCount(int productionMW, int waveNumber, WavesConfig waves)
        {
            int minProduction = math.max(waves.BallisticMinProductionMw, 0);
            int startWave = math.max(waves.BallisticStartWave, 1);
            int mwPerMissile = math.max(waves.BallisticMwPerMissile, 1);
            int maxPerWave = math.max(waves.BallisticMaxPerWave, 0);
            float waveBonusPerWave = math.max(waves.BallisticWaveBonus, 0f);

            // Check minimum thresholds
            if (productionMW < minProduction)
                return 0;

            if (waveNumber < startWave)
                return 0;

            // Base count from production: 1 per 500 MW
            int baseFromProduction = productionMW / mwPerMissile;

            // Bonus from wave progression: +0.5 per wave after start
            int wavesAfterStart = waveNumber - startWave;
            int waveBonus = (int)Math.Round(wavesAfterStart * waveBonusPerWave);

            // Total with cap
            return math.max(0, math.min(baseFromProduction + waveBonus, maxPerWave));
        }

        /// <summary>
        /// Returns the normalized targeting profile used by both runtime spawning and intel display.
        /// All-zero or non-finite profiles fall back to an even split rather than a hidden category bias.
        /// </summary>
        public static (float energy, float critical, float service, float civilian) GetTargetingRatios(
            WaveType waveType,
            WavesConfig waves,
            float energyVariance = 0f,
            bool intro = false)
        {
            float energyBase, criticalBase, serviceBase, civilianBase;

            if (intro)
            {
                // The opening strike is Harassment-typed for intensity but targets like a
                // decisive energy strike, so it reads a dedicated profile rather than the
                // spread Harassment mix. Keyed on the wave's role, not its type, so regular
                // Harassment waves keep the standard targeting split.
                energyBase = waves.IntroEnergyRatio;
                criticalBase = waves.IntroCriticalRatio;
                serviceBase = waves.IntroServiceRatio;
                civilianBase = waves.IntroCivilianRatio;
            }
            else if (waveType == WaveType.MassiveStrike)
            {
                energyBase = waves.MassiveEnergyRatio;
                criticalBase = waves.MassiveCriticalRatio;
                serviceBase = waves.MassiveServiceRatio;
                civilianBase = waves.MassiveCivilianRatio;
            }
            else
            {
                energyBase = waves.TargetEnergyRatio;
                criticalBase = waves.TargetCriticalRatio;
                serviceBase = waves.TargetServiceRatio;
                civilianBase = waves.TargetCivilianRatio;
            }

            energyBase = SanitizeRatio(energyBase);
            criticalBase = SanitizeRatio(criticalBase);
            serviceBase = SanitizeRatio(serviceBase);
            civilianBase = SanitizeRatio(civilianBase);

            float totalBase = energyBase + criticalBase + serviceBase + civilianBase;
            if (totalBase <= float.Epsilon)
            {
                return (DEFAULT_TARGET_RATIO, DEFAULT_TARGET_RATIO, DEFAULT_TARGET_RATIO, DEFAULT_TARGET_RATIO);
            }

            float invTotalBase = math.rcp(totalBase);
            float energy = energyBase * invTotalBase;
            float critical = criticalBase * invTotalBase;
            float service = serviceBase * invTotalBase;
            float civilian = civilianBase * invTotalBase;

            if (!math.isfinite(energyVariance) || math.abs(energyVariance) <= float.Epsilon)
            {
                return (energy, critical, service, civilian);
            }

            float shiftedEnergy = math.saturate(energy + energyVariance);
            float nonEnergy = critical + service + civilian;
            if (nonEnergy <= float.Epsilon)
            {
                return (1f, 0f, 0f, 0f);
            }

            float scale = (1f - shiftedEnergy) * math.rcp(nonEnergy);
            return (shiftedEnergy, critical * scale, service * scale, civilian * scale);
        }

        /// <summary>
        /// Select target category with variance applied.
        /// Variance shifts energy ratio by +/-10%, making Intel valuable.
        /// </summary>
        /// <param name="waveType">Harassment or MassiveStrike</param>
        /// <param name="energyVariance">Variance applied to energy ratio (e.g., -0.1 to +0.1)</param>
        /// <param name="randomRoll">Random value 0-1 for category selection</param>
        /// <returns>Selected target category</returns>
        public static TargetCategory SelectTargetCategory(WaveType waveType, float energyVariance, float randomRoll, WavesConfig waves, bool intro = false)
        {
            var ratios = GetTargetingRatios(waveType, waves, energyVariance, intro);
            float energy = ratios.energy;
            float critical = ratios.critical;
            float service = ratios.service;

            if (randomRoll < energy) return TargetCategory.Energy;
            if (randomRoll < energy + critical) return TargetCategory.Critical;
            if (randomRoll < energy + critical + service) return TargetCategory.Service;
            return TargetCategory.Civilian;
        }

        private static float SanitizeRatio(float value)
        {
            return math.isfinite(value) ? math.max(value, 0f) : 0f;
        }
    }
}
