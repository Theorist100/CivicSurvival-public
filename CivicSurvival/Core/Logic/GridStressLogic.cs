using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure grid-stress arithmetic shared by the runtime (GridStressSystem) and the
    /// in-game crisis sweep. No ECS, no state, no logging — value-in / value-out so the
    /// predicted collapse timing can never drift from the simulated one.
    /// </summary>
    public static class GridStressLogic
    {
        // Fallback grace knobs — mirror balance_config GridStress defaults, used only if config is absent.
        private const float DEFAULT_GRID_GRACE_COEFF = 10f;
        private const float DEFAULT_GRID_GRACE_EXPONENT = 0.5f;
        private const float DEFAULT_GRID_GRACE_REF_POP = 1000f;

        /// <summary>
        /// Population-scaled collapse threshold. Mirrors the manpower cube-root easing
        /// (ManpowerLogic.CalculateBasePool): a small economy gets a wider grace window before
        /// collapse, fading asymptotically to the base CollapseThresholdHours as population grows
        /// so large cities keep the original danger. Input is PopulationPeak (achieved scale, so
        /// the window does not snap back up when residents leave during Crisis/Exodus).
        /// </summary>
        public static float ScaledCollapseThreshold(GridStressConfig gs, int populationPeak)
        {
            float baseThreshold = gs.CollapseThresholdHours;
            if (populationPeak <= 0 || baseThreshold <= 0f)
                return baseThreshold;

            float coeff = gs.GridGraceCoeff >= 0f ? gs.GridGraceCoeff : DEFAULT_GRID_GRACE_COEFF;
            float exponent = gs.GridGraceExponent > 0f ? gs.GridGraceExponent : DEFAULT_GRID_GRACE_EXPONENT;
            float refPop = gs.GridGraceRefPop > 0f ? gs.GridGraceRefPop : DEFAULT_GRID_GRACE_REF_POP;
            float maxHours = math.max(gs.GridGraceMaxHours, baseThreshold);

            // grace shrinks as population rises: (refPop / pop)^exponent → 0 for large pop.
            float multiplier = 1f + coeff * math.pow(refPop / math.max(populationPeak, 1f), exponent);
            return math.clamp(baseThreshold * multiplier, baseThreshold, maxHours);
        }

        /// <summary>
        /// One stress-integration step. Reproduces all three branches of the former
        /// GridStressSystem.UpdateStressAccumulation verbatim. Returns the next StressHours;
        /// <paramref name="collapsed"/> is set true only on the deficit-accumulate branch when
        /// the new value reaches the threshold (>=). Caller writes the return into data.StressHours.
        /// </summary>
        public static float StepStressHours(
            float stressHours, float collapseThresholdHours, bool isDeficit,
            float deltaHours, float stressDecayRate, out bool collapsed)
        {
            collapsed = false;

            // Branch 1 — no threshold (CollapseThresholdHours <= 0): deficit leaves stress
            // FLAT (unchanged); surplus decays toward 0 by the decay-rate. Never collapses.
            if (collapseThresholdHours <= 0f)
                return isDeficit ? stressHours
                                 : math.max(0f, stressHours - deltaHours * stressDecayRate);

            // Branch 2 — post-recovery grace (StressHours < 0): clamp toward 0 by deltaHours,
            // NO decay-rate multiplier, regardless of deficit status. Never collapses.
            if (stressHours < 0f)
                return math.min(0f, stressHours + deltaHours);

            // Branch 3 — normal: deficit accumulates (collapse on >=), surplus decays.
            if (isDeficit)
            {
                float next = stressHours + deltaHours;
                if (next >= collapseThresholdHours)
                    collapsed = true;
                return next;
            }
            return math.max(0f, stressHours - deltaHours * stressDecayRate);
        }
    }
}
