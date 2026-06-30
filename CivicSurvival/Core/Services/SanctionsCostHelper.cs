using System;

namespace CivicSurvival
{
    /// <summary>
    /// Centralized helper for applying sanctions markup to shadow economy costs.
    /// All shadow cost calculations MUST use this helper instead of inline arithmetic.
    /// Pattern: effectiveCost = Round(baseCost × (1 + sanctionsMarkup)).
    /// </summary>
    public static class SanctionsCostHelper
    {
        public static long ApplyMarkup(long baseCost, float sanctionsMarkup)
        {
            double markedUp = Math.Max(0L, baseCost) * (1.0 + Math.Max(0f, sanctionsMarkup));
            if (double.IsNaN(markedUp) || markedUp <= 0.0)
                return 0L;
            if (markedUp >= long.MaxValue)
                return long.MaxValue;
            return (long)Math.Round(markedUp);
        }

        // FIX S26_RAG3:73: Clamp to int range — large baseCost with markup can exceed int.MaxValue
        public static int ApplyMarkup(int baseCost, float sanctionsMarkup)
        {
            double markedUp = Math.Max(0, baseCost) * (1.0 + Math.Max(0f, sanctionsMarkup));
            if (double.IsNaN(markedUp) || markedUp <= 0.0)
                return 0;
            if (markedUp >= int.MaxValue)
                return int.MaxValue;
            return (int)Math.Round(markedUp);
        }
    }
}
