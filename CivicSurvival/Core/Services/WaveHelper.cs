using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Static helper for wave calculations.
    /// Allows cross-domain access without importing Threats.Systems.
    ///
    /// Usage: WaveHelper.GetTargetingRatios(waveState.CurrentWaveType)
    /// </summary>
    public static class WaveHelper
    {
        /// <summary>
        /// Get targeting ratios for a wave. Pass intro:true for the opening strike
        /// (WaveRole.Intro), which targets on its own energy-heavy profile rather than the
        /// Harassment mix its wave type would otherwise select.
        /// Delegates to ThreatMath so intel display and runtime spawning share one profile.
        /// </summary>
        public static (float energy, float critical, float service, float civilian) GetTargetingRatios(WaveType waveType, bool intro = false)
            => ThreatMath.GetTargetingRatios(waveType, BalanceConfig.Current.Waves, 0f, intro);

        /// <summary>
        /// Estimate ballistic missile count based on production capacity and wave progression.
        /// </summary>
        public static int EstimateBallisticCount(int productionMW, int waveNumber)
            => ThreatMath.CalculateBallisticCount(productionMW, waveNumber, BalanceConfig.Current.Waves);
    }
}
