using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer grid-stress / collapse model: the per-tick stability step of the severity sweep.
    /// Applies AutoDispatch shedding with the unsheddable-critical floor, tests the deficit dead-zone,
    /// steps the stress accumulator (<see cref="GridStressLogic.StepStressHours"/>) and drives the
    /// collapse → recovery → grace cycle. NOT new balance arithmetic — the stress step and the collapse
    /// threshold are the same Core.Logic leaves the runtime GridStressSystem uses.
    /// </summary>
    public static class GridStressForecast
    {
        // Post-recovery grace seed (GridStressSystem POST_RECOVERY_GRACE_HOURS const, negative stress).
        private const float POST_RECOVERY_GRACE_HOURS = 1.0f;
        // GridStress dead-zone floor is config'd in kW; the sweep works in MW.
        private const float KW_PER_MW = 1000f;

        /// <summary>
        /// Step the grid-stress / collapse state one tick. Returns true when this tick counts as a
        /// blackout tick (the composer accumulates blackout time on a true return).
        ///
        /// When collapsed: count down the recovery window (the tick is blackout); on recovery, seed the
        /// post-recovery grace (negative stress). Otherwise: shed down to production but never below the
        /// unsheddable-critical floor, classify the deficit against the dead-zone, step the stress, and
        /// record a fresh collapse (with its first-collapse day).
        /// </summary>
        public static bool Step(
            ref ForecastState state,
            float dt, float t,
            float productionFull, float demand, float unsheddable,
            float thr, float panic, float decay, float recovDur,
            float deficitDeadZoneMinKW, float deficitDeadZoneFraction, bool shed)
        {
            if (state.Collapsed)
            {
                state.Recov -= dt;
                if (state.Recov <= 0f)
                {
                    state.Collapsed = false;
                    state.Stress = -POST_RECOVERY_GRACE_HOURS; // grace seed (GridStressSystem const)
                }
                return true; // blackout tick
            }

            // AutoDispatch shedding with the unsheddable-critical floor (KEY 2): shed can close the gap
            // down to production, but a critical fraction can NEVER be dropped, so once production <
            // demand·UNSHEDDABLE_FRAC the deficit stands.
            float stressPct = thr > 0f ? state.Stress / thr : 0f;
            bool shedding = shed && stressPct >= panic && productionFull < demand;
            float active = shedding
                ? math.max(math.min(demand, productionFull), unsheddable)
                : demand;

            // Same deficit rule as the runtime (GridStressMath), fed the forecast's own input:
            // productionFull−active as the signed balance and the MW-converted dead-zone floor.
            float deadZoneMinMW = deficitDeadZoneMinKW / KW_PER_MW;
            bool isDeficit = GridStressMath.IsDeficit(productionFull - active, deadZoneMinMW, deficitDeadZoneFraction, active);

            state.Stress = GridStressLogic.StepStressHours(state.Stress, thr, isDeficit, dt, decay, out bool collapsedNow);
            if (collapsedNow)
            {
                state.Collapsed = true;
                state.Recov = recovDur;
                // Day INDEX from elapsed game-hours — floor is the correct day number (CIVIC177).
                if (state.FirstCollapseDay < 0)
                    state.FirstCollapseDay = (int)math.floor(t / GameRate.HOURS_PER_DAY);
            }

            return state.Collapsed;
        }
    }
}
