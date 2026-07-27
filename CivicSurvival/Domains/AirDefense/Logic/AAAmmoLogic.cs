using System;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Pure ammo calculation logic for Air Defense.
    /// No ECS dependencies — all data passed as parameters.
    /// Burst-compatible static methods.
    /// </summary>
    internal static class AAAmmoLogic
    {
        /// <summary>
        /// Unified tariff: the per-round price of an AA type, derived from its flat emergency
        /// ResupplyCost spread across the type's city-wide live magazine capacity. Refilling a
        /// type from empty then costs its emergency-button price no matter which path delivered
        /// the rounds — the auto trickle stops overcharging relative to the manual button
        /// (pre-unification: 2000/round flat-rate vs 8000 per whole type; a five-gun Bofors park
        /// cost 500k on auto vs 8k on the button). Min 1 so a paid tariff never rounds to free.
        /// </summary>
        public static int PerRoundCostFromFlat(int flatCost, int totalTypeCapacity)
        {
            if (flatCost <= 0 || totalTypeCapacity <= 0) return 0;
            return Math.Max(1, (int)Math.Round((double)flatCost / totalTypeCapacity));
        }

        /// <summary>
        /// Clamp a per-line delivery plan to the available money, in line order (the same
        /// first-AA-first priority DistributeRounds used). Mutates <paramref name="give"/> in
        /// place: the first unaffordable line is cut to the affordable remainder, every later
        /// PAID line is zeroed; free lines (costPerRound &lt;= 0) always survive. Returns the
        /// total cost of the clamped plan.
        /// </summary>
        public static long ClampPlanToBudget(int[] give, int[] costPerRound, long availableMoney)
        {
            long total = 0;
            bool exhausted = false;
            for (int i = 0; i < give.Length; i++)
            {
                if (give[i] <= 0) continue;
                int cost = i < costPerRound.Length ? costPerRound[i] : 0;
                if (cost <= 0) continue; // free rounds are never budget-limited

                if (exhausted)
                {
                    give[i] = 0;
                    continue;
                }

                long lineCost = (long)give[i] * cost;
                if (total + lineCost <= availableMoney)
                {
                    total += lineCost;
                    continue;
                }

                long remaining = availableMoney - total;
                // Min against the planned line: reaching this branch means the full line was NOT
                // affordable, so the quotient is always below give[i] — the clamp also keeps the
                // long→int narrowing provably in range (CIVIC136).
                int affordable = remaining > 0 ? (int)Math.Min(give[i], remaining / cost) : 0;
                give[i] = affordable;
                total += (long)affordable * cost;
                exhausted = true;
            }
            return total;
        }

    }
}
