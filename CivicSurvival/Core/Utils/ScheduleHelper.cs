using Unity.Burst;
using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Centralized schedule blackout logic with automatic phase offset.
    /// Used by BlackoutJob (Burst), BlackoutData, and managed systems.
    ///
    /// Phase Offset: Each district is shifted in time to prevent
    /// all districts from going OFF simultaneously.
    ///
    /// IMPORTANT: Must be Burst-compiled for use in Jobs.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public static class ScheduleHelper
    {
        /// <summary>
        /// Check if blackout is active for given schedule, hour, and district.
        /// Uses automatic phase offset for cyclic schedules.
        /// </summary>
        /// <param name="scheduleId">Schedule preset ID (0-4)</param>
        /// <param name="gameHour">Current game hour (0-24)</param>
        /// <param name="districtIndex">District index for phase offset calculation</param>
        /// <returns>True if blackout should be active (power OFF)</returns>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public static bool IsBlackoutActive(int scheduleId, float gameHour, int districtIndex)
        {
            return GameHourOfDay.TryNormalize(gameHour, out var hour)
                && IsBlackoutActive(scheduleId, hour, districtIndex);
        }

        /// <summary>
        /// Check if blackout is active for a typed normalized game hour.
        /// </summary>
        // No [BurstCompile] on this typed overload — BC1064 rejects struct
        // parameters in Burst external entries. Burst jobs that call this method
        // (BlackoutJob, NeighborEnvyJob) inline its body via the caller's own
        // [BurstCompile], where struct params are fine. The float overload above
        // keeps [BurstCompile] for the primitive entry point.
        public static bool IsBlackoutActive(int scheduleId, GameHourOfDay gameHour, int districtIndex)
        {
            // Manual = no scheduled blackout
            if (scheduleId == 0)
                return false;

            int hour = (int)math.floor(gameHour.Value) % (int)GameRate.HOURS_PER_DAY;

            switch (scheduleId)
            {
                case 1: // MildRestriction (33% saving)
                    return IsCyclicBlackout(hour, onDuration: Engine.LoadShedding.MILD_ON_HOURS, offDuration: Engine.LoadShedding.MILD_OFF_HOURS, districtIndex);

                case 2: // Balanced (50% saving)
                    return IsCyclicBlackout(hour, onDuration: Engine.LoadShedding.BALANCED_ON_HOURS, offDuration: Engine.LoadShedding.BALANCED_OFF_HOURS, districtIndex);

                case 3: // SevereCrisis (66% saving)
                    return IsCyclicBlackout(hour, onDuration: Engine.LoadShedding.SEVERE_ON_HOURS, offDuration: Engine.LoadShedding.SEVERE_OFF_HOURS, districtIndex);

                case 4: // DayShift - ON 08:00-20:00, OFF 20:00-08:00
                    return hour < Engine.LoadShedding.DAYSHIFT_START_HOUR || hour >= Engine.LoadShedding.DAYSHIFT_END_HOUR;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Backward compatibility overload (without districtIndex).
        /// Uses districtIndex=0 (no offset).
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public static bool IsBlackoutActive(int scheduleId, float gameHour)
        {
            return IsBlackoutActive(scheduleId, gameHour, 0);
        }

        /// <summary>
        /// Calculate if blackout is active for cyclic ON/OFF schedule with phase offset.
        ///
        /// Phase offset ensures different districts are in different phases:
        /// - District 0: OFF at hours 0-2 (for 4on/2off)
        /// - District 1: OFF at hours 1-3
        /// - District 2: OFF at hours 2-4
        /// etc.
        ///
        /// This prevents all districts from going dark simultaneously.
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        private static bool IsCyclicBlackout(int hour, int onDuration, int offDuration, int districtIndex)
        {
            int cycle = onDuration + offDuration;

            // UTL-006 FIX: Guard against division-by-zero if cycle == 0
            // (shouldn't happen with valid schedules, but defensive programming)
            if (cycle <= 0)
                return false; // Treat as "always ON" (no blackout)

            int offset = GetDistrictPhaseOffset(districtIndex, cycle);
            int shiftedHour = (hour + offset) % cycle;

            // Position within the cycle
            int positionInCycle = shiftedHour;

            // Power is ON for first 'onDuration' hours of cycle
            // Power is OFF for remaining 'offDuration' hours
            return positionInCycle >= onDuration;
        }

        private static int GetDistrictPhaseOffset(int districtIndex, int cycle)
        {
            const int PHASE_STRIDE = 7; // Coprime with 24, 8, and 6.
            int safeCycle = math.max(cycle, 1);
            int offset = (districtIndex * PHASE_STRIDE + districtIndex / safeCycle) % safeCycle;
            if (offset < 0)
                offset += safeCycle;
            return offset;
        }
    }
}
