using System;
using Unity.Mathematics;

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
        /// Calculate total rounds needed to fully resupply.
        /// </summary>
        /// <param name="aaUnits">Array of (currentAmmo, maxAmmo) for each AA</param>
        /// <returns>Total rounds needed</returns>
        public static int CalculateTotalRoundsNeeded((int current, int max)[] aaUnits)
        {
            int total = 0;
            foreach (var (current, max) in aaUnits)
            {
                total += Math.Max(0, max - current); // H5: corrupt save edge case (current > max) must not produce negative cost
            }
            return total;
        }

        /// <summary>
        /// Calculate cost to resupply given rounds.
        /// </summary>
        public static long CalculateResupplyCost(int rounds, int costPerRound)
        {
            return (long)rounds * costPerRound;
        }

        /// <summary>
        /// Calculate how many rounds can be afforded.
        /// </summary>
        /// <remarks>
        /// INVARIANT: Must only be called after EvaluateResupply confirms costPerRound > 0.
        /// EvaluateResupply returns Full (cost=0) before reaching this path when costPerRound=0,
        /// so int.MaxValue is never passed to DistributeRounds in practice.
        /// If called directly with costPerRound=0, int.MaxValue can overflow DistributeRounds.
        /// </remarks>
        public static int CalculateAffordableRounds(long availableMoney, int costPerRound)
        {
            if (costPerRound <= 0) return int.MaxValue; // Free ammo — unlimited rounds (see INVARIANT)
            if (availableMoney <= 0) return 0; // Negative balance → no affordable rounds
            long rounds = availableMoney / costPerRound;
            return rounds > int.MaxValue ? int.MaxValue : (int)rounds;
        }

        /// <summary>
        /// Distribute rounds across AA units (priority order).
        /// Returns array of rounds to give to each unit.
        /// </summary>
        /// <param name="aaUnits">Array of (currentAmmo, maxAmmo) for each AA</param>
        /// <param name="totalRoundsToDistribute">Total rounds available</param>
        /// <returns>Array of rounds to add to each AA unit</returns>
        public static int[] DistributeRounds((int current, int max)[] aaUnits, int totalRoundsToDistribute)
        {
            // int.MaxValue (from costPerRound=0 path) would cause silent all-zero or overflow.
            // Caller invariant says this shouldn't happen, but guard defensively.
            if (totalRoundsToDistribute == int.MaxValue || totalRoundsToDistribute < 0)
            {
                Mod.Log.Error($"DistributeRounds: invalid totalRoundsToDistribute={totalRoundsToDistribute}");
                return new int[aaUnits.Length];
            }

            var result = new int[aaUnits.Length];
            int roundsLeft = totalRoundsToDistribute;

            for (int i = 0; i < aaUnits.Length && roundsLeft > 0; i++)
            {
                int needed = aaUnits[i].max - aaUnits[i].current;
                if (needed <= 0) continue; // H6: corrupt save edge case (current > max)
                int give = math.min(needed, roundsLeft);
                result[i] = give;
                roundsLeft -= give;
            }

            return result;
        }

        /// <summary>
        /// Aggregate AA statistics.
        /// </summary>
        /// <param name="aaUnits">Array of (currentAmmo, maxAmmo)</param>
        /// <returns>(totalAmmo, totalMaxAmmo, count)</returns>
        public static (int total, int max, int count) AggregateStats((int current, int max)[] aaUnits)
        {
            int total = 0;
            int maxSum = 0;
            foreach (var (current, max) in aaUnits)
            {
                int safeMax = Math.Max(0, max);
                int safeCurrent = math.clamp(current, 0, safeMax);
                total += safeCurrent;
                maxSum += safeMax;
            }
            return (total, maxSum, aaUnits.Length);
        }

        /// <summary>
        /// Determine resupply result based on available funds.
        /// </summary>
        public static ResupplyResult EvaluateResupply(
            int totalRoundsNeeded,
            long availableMoney,
            int costPerRound)
        {
            if (totalRoundsNeeded < 0)
                return new ResupplyResult { Status = ResupplyStatus.NotNeeded, RoundsToResupply = 0, Cost = 0 }; // H5: corrupt save guard
            if (totalRoundsNeeded == 0)
            {
                return new ResupplyResult
                {
                    Status = ResupplyStatus.NotNeeded,
                    RoundsToResupply = 0,
                    Cost = 0
                };
            }

            // Free ammo: always Full regardless of available balance (negative balance allowed)
            if (costPerRound <= 0)
            {
                return new ResupplyResult
                {
                    Status = ResupplyStatus.Full,
                    RoundsToResupply = totalRoundsNeeded,
                    Cost = 0
                };
            }

            long totalCost = CalculateResupplyCost(totalRoundsNeeded, costPerRound);

            if (availableMoney >= totalCost)
            {
                return new ResupplyResult
                {
                    Status = ResupplyStatus.Full,
                    RoundsToResupply = totalRoundsNeeded,
                    Cost = totalCost
                };
            }

            int affordableRounds = CalculateAffordableRounds(availableMoney, costPerRound);

            if (affordableRounds > 0)
            {
                return new ResupplyResult
                {
                    Status = ResupplyStatus.Partial,
                    RoundsToResupply = affordableRounds,
                    Cost = CalculateResupplyCost(affordableRounds, costPerRound)
                };
            }

            return new ResupplyResult
            {
                Status = ResupplyStatus.Failed,
                RoundsToResupply = 0,
                Cost = 0,
                RequiredCost = totalCost
            };
        }
    }

    /// <summary>
    /// Result of resupply evaluation.
    /// </summary>
    internal struct ResupplyResult
    {
        public ResupplyStatus Status;
        public int RoundsToResupply;
        public long Cost;
        public long RequiredCost; // Only for Failed status
    }

    /// <summary>
    /// Resupply operation status.
    /// </summary>
    internal enum ResupplyStatus
    {
        NotNeeded = 0,
        Full,
        Partial,
        Failed
    }
}
