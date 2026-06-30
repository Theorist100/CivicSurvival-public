using Unity.Burst;
using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: blackout stress pressure from power status.
    /// Three-layer coverage-based mitigation: Hospital/School/Private coverage
    /// reduces stress proportionally, gated by BackupPolicy.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class BlackoutCalculator
    {
        /// <summary>After 4 hours, decay rate jumps 10x</summary>
        public const float PATIENCE_THRESHOLD_HOURS = 4f;

        /// <summary>-1%/hr in 0-1 scale (patience zone)</summary>
        public const float PATIENCE_RATE = 0.01f;

        /// <summary>-10%/hr in 0-1 scale (panic zone)</summary>
        public const float PANIC_RATE = 0.10f;

        /// <summary>
        /// Calculate blackout pressure with three-layer coverage-based mitigation.
        /// </summary>
        /// <param name="hasPower">True if building has power (fulfilled > 0 or no consumer)</param>
        /// <param name="policy">Current backup discharge policy</param>
        /// <param name="deltaHours">Time delta in hours</param>
        /// <param name="hospitalCov">Hospital battery coverage ratio (0-1)</param>
        /// <param name="schoolCov">School battery coverage ratio (0-1)</param>
        /// <param name="privateCov">Private battery coverage ratio (0-1)</param>
        /// <param name="wHosp">Hospital weight in mitigation formula</param>
        /// <param name="wSchool">School weight in mitigation formula</param>
        /// <param name="wPriv">Private weight in mitigation formula</param>
        /// <param name="mitigationMin">Minimum stress multiplier (never fully zero)</param>
        /// <param name="blackoutHours">Accumulated blackout hours (updated)</param>
        /// <param name="pressureBlackout">Output pressure value (0-1)</param>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static void Calculate(
            bool hasPower,
            BackupPolicy policy,
            float deltaHours,
            float hospitalCov,
            float schoolCov,
            float privateCov,
            float wHosp,
            float wSchool,
            float wPriv,
            float mitigationMin,
            ref float blackoutHours,
            out float pressureBlackout)
        {
            if (hasPower)
            {
                blackoutHours = 0f;
                pressureBlackout = 0f;
                return;
            }

            // Accumulate blackout duration
            const float MAX_BLACKOUT_HOURS = 100_000f;
            float prevBlackoutHours = math.max(0f, blackoutHours);
            blackoutHours = math.min(prevBlackoutHours + deltaHours, MAX_BLACKOUT_HOURS);

            // FIX S4-06: Split-calc when threshold crossed within single delta.
            // Without this, the 10x rate jump (patience→panic) applies to the FULL delta
            // instead of only the portion above threshold — overshoot at fast game speeds.
            float rate;
            if (prevBlackoutHours < PATIENCE_THRESHOLD_HOURS && blackoutHours >= PATIENCE_THRESHOLD_HOURS && deltaHours > 0f)
            {
                float subThresholdPortion = math.clamp(PATIENCE_THRESHOLD_HOURS - prevBlackoutHours, 0f, deltaHours);
                float overThresholdPortion = math.max(0f, deltaHours - subThresholdPortion);
#pragma warning disable CIVIC100 // Guarded by deltaHours > 0f on line 70
                rate = (PATIENCE_RATE * subThresholdPortion + PANIC_RATE * overThresholdPortion) / deltaHours;
#pragma warning restore CIVIC100
            }
            else
            {
                rate = blackoutHours < PATIENCE_THRESHOLD_HOURS ? PATIENCE_RATE : PANIC_RATE;
            }
            rate = math.clamp(rate, PATIENCE_RATE, PANIC_RATE);

            // Coverage-based mitigation gated by policy
            float effectiveHospital = policy >= BackupPolicy.CriticalOnly ? hospitalCov : 0f;
            float effectiveSchool = policy >= BackupPolicy.CriticalOnly ? schoolCov : 0f;
            float effectivePrivate = policy == BackupPolicy.FullDischarge ? privateCov : 0f;

            float mitigation = 1.0f
                - (effectiveHospital * wHosp)
                - (effectiveSchool * wSchool)
                - (effectivePrivate * wPriv);

            mitigation = math.clamp(mitigation, mitigationMin, 1.0f);

            pressureBlackout = rate * mitigation;
        }

    }
}
