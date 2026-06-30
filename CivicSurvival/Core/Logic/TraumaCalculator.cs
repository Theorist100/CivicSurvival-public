using Unity.Burst;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: trauma and recovery inertia.
    /// Extracted from MentalHealthJobs.UpdatePsyStateJob.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class TraumaCalculator
    {
        /// <summary>
        /// Calculate trauma and recovery inertia updates.
        /// </summary>
        /// <param name="pressureBlackout">Blackout pressure this frame (0-1)</param>
        /// <param name="pressureEnvy">Envy pressure this frame (0-1) — neighbor has power, you don't</param>
        /// <param name="pressureImpact">Impact pressure this frame (0-1) — rocket/ballistic hit nearby</param>
        /// <param name="alarmistStressRate">Extra stress from Alarmist mode (per hour)</param>
        /// <param name="deltaTime">Time delta in seconds</param>
        /// <param name="deltaHours">Time delta in hours</param>
        /// <param name="traumaGainRate">Config: how fast trauma accumulates (per second — used with deltaTime)</param>
        /// <param name="traumaDecayRate">Config: how fast trauma decays (per second — used with deltaTime)</param>
        /// <param name="inertiaGainRate">Config: recovery inertia gain during blackout (per hour — used with deltaHours)</param>
        /// <param name="inertiaDecayRate">Config: recovery inertia decay after power (per hour — used with deltaHours)</param>
        /// <param name="trauma">Current trauma (updated)</param>
        /// <param name="recoveryInertia">Current recovery inertia (updated)</param>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
#pragma warning disable CIVIC299 // Intentional: traumaGainRate/decayRate are per-second, alarmistStressRate/inertiaRates are per-hour — different physical quantities require different time bases
        public static void Calculate(
            float pressureBlackout,
            float pressureEnvy,
            float pressureImpact,
            float alarmistStressRate,
            float deltaTime,
            float deltaHours,
            float traumaGainRate,
            float traumaDecayRate,
            float inertiaGainRate,
            float inertiaDecayRate,
            ref float trauma,
            ref float recoveryInertia)
        {
            // Trauma: gain from Blackout/Envy/Impact pressure, decay over time
            // Alarmist mode adds baseline stress (watching scary news all day is exhausting)
            float pressureSum = math.saturate(pressureBlackout)
                + math.saturate(pressureEnvy)
                + math.saturate(pressureImpact);
            float pressureGain = pressureSum * math.max(0f, traumaGainRate) * math.max(0f, deltaTime);
            float alarmistGain = math.max(0f, alarmistStressRate) * math.max(0f, deltaHours);
            trauma = math.saturate(trauma + pressureGain + alarmistGain);
            trauma -= math.max(0f, traumaDecayRate) * math.max(0f, deltaTime);
            trauma = math.saturate(trauma);

            // Recovery inertia: accumulates during blackout, decays after power restored
            // "People are still scared" - prevents instant wellbeing jump
            if (pressureBlackout > 0f)
            {
                // Blackout active: inertia grows proportional to stress rate
#pragma warning disable CIVIC056 // Clamped to 0-1 via math.min below — no unbounded growth
                recoveryInertia += math.saturate(pressureBlackout) * math.max(0f, inertiaGainRate) * math.max(0f, deltaHours);
#pragma warning restore CIVIC056
                recoveryInertia = math.min(1f, recoveryInertia);
            }
            else
            {
                // Power restored: inertia decays
                recoveryInertia -= math.max(0f, inertiaDecayRate) * math.max(0f, deltaHours);
                recoveryInertia = math.max(0f, recoveryInertia);
            }
        }
#pragma warning restore CIVIC299
    }
}
