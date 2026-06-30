using Unity.Burst;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: household resistance from education.
    /// Extracted from MentalHealthJobs.CalculateResistanceJob.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class ResistanceCalculator
    {
        /// <summary>Maximum resistance value (80%)</summary>
        public const float MAX_RESISTANCE = 0.8f;

        /// <summary>Education to resistance conversion factor (0.2 per education level)</summary>
        private const float EDUCATION_RESISTANCE_FACTOR = 0.2f;

        /// <summary>Recalculation interval in seconds (~30 sec)</summary>
        public const float RECALC_INTERVAL_SECONDS = 30.0f;

        /// <summary>
        /// Calculate resistance from average education level.
        /// Education 0-4 maps to resistance 0-0.8.
        /// </summary>
        /// <param name="avgEducation">Average education level of household (0-4)</param>
        /// <returns>Resistance value (0-0.8)</returns>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static float FromEducation(float avgEducation)
        {
            // Education 0-4 -> Resistance 0-0.8 (clamped to MAX_RESISTANCE)
            return math.min(avgEducation * EDUCATION_RESISTANCE_FACTOR, MAX_RESISTANCE);
        }

        /// <summary>
        /// Check if resistance should be recalculated.
        /// </summary>
        /// <param name="currentTime">Monotonic game time in seconds (TotalGameHours * 3600)</param>
        /// <param name="lastUpdateTime">Game time when resistance was last calculated (persisted)</param>
        /// <returns>True if recalculation needed</returns>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static bool ShouldRecalculate(float currentTime, float lastUpdateTime)
        {
            // First run or clock rewind (defensive guard) = recalculate immediately
            if (lastUpdateTime <= 0f || currentTime < lastUpdateTime)
                return true;

            return currentTime - lastUpdateTime >= RECALC_INTERVAL_SECONDS;
        }
    }
}
