using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure static surplus-saturation math. No ECS / no state. Shared by
    /// PowerCapacityResolverSystem (per-tick fleet factor) and the Фаза-4 UI
    /// aggregate (fleet KPD). Mirrors GridZoneCalculator's pure-function style.
    /// </summary>
    public static class SaturationLogic
    {
        private const float EPSILON = 0.001f;

        /// <summary>
        /// Target saturation factor ∈ [floor, 1] from built nameplate vs demand.
        /// allowed = max(demand*headroom, demand+unitBufferMW);
        /// headroom = headroomBase + headroomPerType*intermittentTypes.
        /// <paramref name="intermittentTypes"/> counts only weather-dependent generation
        /// types present (Wind/Solar — see <c>PowerPlantUtils.IsIntermittent</c>): they
        /// genuinely need backup reserve, so each widens the forgiven headroom. Stable
        /// types grant nothing — counting every brand let "diverse spam" farm the
        /// threshold (7 types → ×3.4 forgiven). Natural ceiling: 2 intermittent types
        /// exist, so headroom tops out at base + 2·perType without a clamp knob.
        /// <paramref name="unitBufferMW"/> is the N+1 unit buffer — the caller passes
        /// min(largest grid-producer nameplate, UnitBufferCapMW): a reserve of one biggest
        /// plant is forgiven (plants come in build quanta; losing the biggest unit must not
        /// black out the city), the cap closes the single-giant-plant loophole.
        /// </summary>
        public static float ComputeTargetFactor(
            float totalNameplateMW, float demandMW, int intermittentTypes,
            float headroomBase, float headroomPerType,
            float softness, float floor, float unitBufferMW)
        {
            float headroom = headroomBase + headroomPerType * math.max(0, intermittentTypes);
            float allowed = math.max(demandMW * headroom, demandMW + unitBufferMW);
            float ratio = totalNameplateMW / math.max(allowed, EPSILON);
            float over = math.max(0f, ratio - 1f);
            // denom = 1 + softness·over. With sane config (softness ≥ 0, over ≥ 0) it is ≥ 1; the
            // max-guard keeps it strictly positive even if softness is mis-configured negative,
            // so the division can never hit zero / produce NaN.
            float denom = math.max(EPSILON, 1f + softness * over);
            float target = 1f / denom;
            return math.clamp(target, floor, 1f);
        }

        /// <summary>
        /// Asymmetric inertia step: down instant, up exponential over tauUpHours.
        /// The hysteresis band is direction-aware: DOWN inside the band holds (anti-pulsation
        /// — the instant drop is the only move that can flutter on target jitter), UP inside
        /// the band ARRIVES at the target exactly. A symmetric dead zone froze every recovery
        /// at target−hysteresis forever (~97% with the 0.03 default) while the UI said
        /// "recovered" — <c>EstimateRecoveryHours</c> already treats the band as
        /// "close enough = arrived", and this makes the step function honour that contract.
        /// Returns the new effective factor.
        /// </summary>
        public static float StepInertia(
            float current, float target, float deltaHours,
            float hysteresis, float tauUpHours)
        {
            float gap = target - current;
            if (math.abs(gap) <= hysteresis)
                return gap > 0f ? target : current;      // up: arrive; down: hold (anti-flutter)
            if (gap < 0f)
                return target;                           // down — instant
            float dh = math.max(0f, deltaHours);
            float alpha = 1f - math.exp(-dh / math.max(EPSILON, tauUpHours));
            return current + gap * alpha;                // up — slow
        }
    }
}
