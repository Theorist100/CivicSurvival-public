using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure targeting logic for Air Defense.
    /// No ECS dependencies — all data passed as parameters.
    ///</summary>
    internal static class AALogic
    {
        private static readonly LogContext Log = new("AALogic");

        // ===== Ammo Thresholds =====
        private const float LOW_AMMO_RATIO = 0.2f;
        private const float LOW_AMMO_PENALTY = 0.2f;

        /// <summary>
        /// Calculate intercept chance with all modifiers including Evasive Maneuvers.
        /// </summary>
        /// <param name="baseChance">Base intercept chance from AA component</param>
        /// <param name="currentAmmo">Current ammo count</param>
        /// <param name="maxAmmo">Maximum ammo capacity</param>
        /// <param name="spotterPenalty">Penalty from active spotters (0-1)</param>
        /// <param name="missedShotsCount">Number of shots this threat has evaded (Evasive Maneuvers)</param>
        /// <param name="detectionBonus">Bonus from Telemarathon Alarmist mode (vigilant citizens)</param>
        /// <returns>Final intercept chance (0-1)</returns>
        public static float CalculateInterceptChance(
            float baseChance,
            int currentAmmo,
            int maxAmmo,
            float spotterPenalty,
            int missedShotsCount = 0,
            float detectionBonus = 0f)
        {
            float chance = baseChance;

            // Telemarathon Alarmist mode bonus (vigilant citizens help spot threats).
            // This is intentionally additive with the low-ammo penalty below; ordering does not change
            // the final chance, and keeping both terms explicit makes balance tuning easier to read.
            chance += detectionBonus;

            // Low ammo penalty (panic shooting)
            if (maxAmmo > 0 && currentAmmo < maxAmmo * LOW_AMMO_RATIO) // H7: guard against NaN when maxAmmo==0
            {
#pragma warning disable CIVIC194 // chance clamped to [minChance,1] at end of method via math.clamp
                chance -= LOW_AMMO_PENALTY;
#pragma warning restore CIVIC194
            }

            // Spotter penalty (Valera posts AA positions on Telegram)
            chance -= spotterPenalty;

            // Evasive Maneuvers: drone learns from each miss, harder to hit
            // This fixes "Deathball" problem where many AA = 100% intercept
            var adCfg = BalanceConfig.Current.AirDefense;
            float evasionPenalty = missedShotsCount * adCfg.EvasionPerShot;
            chance -= evasionPenalty;

            // Hard floor: always 5% "lucky shot" chance even if drone is dodging like crazy
            float minChance = adCfg.EvasionMinChance;

            // Log if evasion is significant (helps balance tuning)
            if (evasionPenalty > adCfg.EvasionLogThreshold)
            {
                if (Log.IsDebugEnabled) Log.Debug($"High evasion: {missedShotsCount} misses = -{evasionPenalty:P0} penalty, effective chance: {math.max(minChance, chance):P0}");
            }

            return math.clamp(chance, minChance, 1f);
        }
    }
}
