using System;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure refund math for demolishing a player-paid AA installation — a faithful mirror of
    /// vanilla's <c>ObjectUtils.GetRefundAmount</c> buyer's-remorse model: a three-tier step
    /// function over elapsed game-days since placement that decays to zero past the last
    /// window. Demolishing soon after placing returns most of the cost (mis-click protection);
    /// waiting decays the refund to nothing, which is what makes a place→demolish→replace loop
    /// non-profitable without any extra anti-exploit code.
    ///
    /// Pure function over blittable int/float: returns the refund amount, the CALLER emits the
    /// budget request. Side-effect-free so the same transition is reusable by a future server
    /// recompute (forecast/PvP parity), next to <see cref="CrewMath"/>.
    /// </summary>
    public static class AARefund
    {
        /// <summary>
        /// Refund for a demolished installation. <paramref name="paidBudget"/> is the cash
        /// actually charged at placement (0 for credit/Heritage placements → 0 refund, which
        /// closes the credit→cash money-printing hole). The percentage is picked by how many
        /// game-days elapsed since placement, stepping down through the three configured tiers
        /// and reaching 0 after the last window.
        /// </summary>
        public static int Compute(int paidBudget, float placedGameHours, float currentGameHours,
            AAUnitsConfig cfg)
        {
            if (paidBudget <= 0)
                return 0;

            float elapsedDays = GameRate.DayFractionFromHours(
                Math.Max(0f, currentGameHours - placedGameHours));

            float pct = 0f;
            if (elapsedDays < cfg.RefundWindowDays1)
                pct = cfg.RefundPercent1;
            else if (elapsedDays < cfg.RefundWindowDays2)
                pct = cfg.RefundPercent2;
            else if (elapsedDays < cfg.RefundWindowDays3)
                pct = cfg.RefundPercent3;

            // Never refund more than was paid, never negative — a typo in the config tiers
            // must not print money or claw cash back.
            pct = Math.Clamp(pct, 0f, 1f);
            if (pct <= 0f)
                return 0;

            return (int)Math.Round(paidBudget * (double)pct);
        }
    }
}
