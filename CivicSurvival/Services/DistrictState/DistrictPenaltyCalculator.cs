using System;
using Unity.Mathematics;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Calculator for district penalties.
    /// Stateless - all methods are pure functions.
    /// </summary>
    public static class DistrictPenaltyCalculator
    {
        // Cached enum values to avoid GC allocation
        private static readonly PenaltySource[] s_PenaltySources =
            (PenaltySource[])Enum.GetValues(typeof(PenaltySource));

        /// <summary>
        /// Recalculate total penalties from active sources.
        /// </summary>
        public static void Recalculate(ref DistrictPenalties penalties)
        {
            float happiness = 0f;
            float commerce = 0f;

            foreach (var source in s_PenaltySources)
            {
                if (source == PenaltySource.None || source == PenaltySource.AllFlags || !IsSingleFlag(source))
                    continue;

                if ((penalties.ActiveSources & source) == 0)
                    continue;

                if (PenaltyConfig.Penalties.TryGetValue(source, out var config))
                {
                    happiness += config.Happiness;
                    commerce += config.Commerce;
                }
            }

            // Apply caps
            penalties.TotalHappinessPenalty = math.min(happiness, PenaltyConfig.MAX_HAPPINESS_PENALTY);
            penalties.TotalCommercePenalty = math.min(commerce, PenaltyConfig.MAX_COMMERCE_PENALTY);
        }

        /// <summary>
        /// Calculate district happiness penalties for consumers where negative
        /// local bonuses, such as FoodAidProvided, must not offset citywide logic.
        /// </summary>
        public static float CalculatePositiveHappinessPenalty(in DistrictPenalties penalties)
        {
            float happiness = 0f;

            foreach (var source in s_PenaltySources)
            {
                if (source == PenaltySource.None || source == PenaltySource.AllFlags || !IsSingleFlag(source))
                    continue;

                if ((penalties.ActiveSources & source) == 0)
                    continue;

                if (PenaltyConfig.Penalties.TryGetValue(source, out var config) && config.Happiness > 0f)
                    happiness += config.Happiness;
            }

            return math.clamp(happiness, 0f, PenaltyConfig.MAX_HAPPINESS_PENALTY);
        }

        /// <summary>
        /// Add a penalty source and recalculate totals.
        /// Returns true if source was added (wasn't already present).
        /// </summary>
        public static bool AddSource(ref DistrictPenalties penalties, PenaltySource source)
        {
            source = PenaltySources.Sanitize((int)source);
            if (source == PenaltySource.None)
                return false;

            var missing = source & ~penalties.ActiveSources;
            if (missing == PenaltySource.None)
                return false;

            penalties.ActiveSources |= missing;
            Recalculate(ref penalties);
            return true;
        }

        /// <summary>
        /// Remove a penalty source and recalculate totals.
        /// Returns true if source was removed (was present).
        /// </summary>
        public static bool RemoveSource(ref DistrictPenalties penalties, PenaltySource source)
        {
            source = PenaltySources.Sanitize((int)source);
            if (source == PenaltySource.None)
                return false;

            var present = penalties.ActiveSources & source;
            if (present == PenaltySource.None)
                return false;

            penalties.ActiveSources &= ~present;
            Recalculate(ref penalties);
            return true;
        }

        /// <summary>
        /// Check if penalties are empty (no active sources).
        /// </summary>
        public static bool IsEmpty(in DistrictPenalties penalties)
        {
            return penalties.ActiveSources == PenaltySource.None;
        }

        private static bool IsSingleFlag(PenaltySource source)
        {
            int value = (int)source;
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
