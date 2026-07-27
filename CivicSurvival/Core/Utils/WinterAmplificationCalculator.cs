using CivicSurvival.Core.Config;
using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Single source of truth for winter consumption amplification formula.
    /// Called by WinterMultiplierSystem (observer) and WinterMultiplierPatch (Harmony applicator).
    /// </summary>
    public static class WinterAmplificationCalculator
    {
        private const float AMPLIFICATION_SEVERITY_COEFF = 2.0f;
        private const float AMPLIFICATION_CLAMP_MIN = 1f;

        /// <summary>
        /// Calculate winter consumption amplification based on temperature and severity.
        /// Returns 1.0 for no amplification (warm weather), up to config max (extreme cold).
        /// </summary>
        public static float Calculate(float temperature, float winterSeverity)
            => Calculate(temperature, winterSeverity, BalanceConfig.Current.Engineering.WinterMultMax);

        /// <summary>
        /// Calculate winter consumption amplification using an explicit max multiplier.
        /// Use this overload from hot paths that already have a balance snapshot.
        /// </summary>
        public static float Calculate(float temperature, float winterSeverity, float winterMultMax)
        {
            if (!math.isfinite(temperature) || !math.isfinite(winterSeverity) || !math.isfinite(winterMultMax))
                return 1f;

            if (temperature >= 10f)
                return 1f;

            float maxAmplification = math.clamp(
                1f + (AMPLIFICATION_SEVERITY_COEFF * winterSeverity),
                AMPLIFICATION_CLAMP_MIN,
                math.max(winterMultMax, AMPLIFICATION_CLAMP_MIN));
            float midAmplification = 1f + ((maxAmplification - 1f) * 0.5f);

            if (temperature <= -10f)
                return maxAmplification;

            if (temperature >= 0f)
            {
                float t = (10f - temperature) / 10f;
                return 1f + ((midAmplification - 1f) * t);
            }

            float coldT = (-temperature) / 10f;
            return midAmplification + ((maxAmplification - midAmplification) * coldT);
        }
    }
}
