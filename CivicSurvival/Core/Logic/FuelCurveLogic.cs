using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure static fuel-stockpile sigmoid for thermal plants. No ECS / no state. Mirrors
    /// <see cref="SaturationLogic"/>'s pure-function style.
    ///
    /// Replaces vanilla's binary "any fuel → 100%, none → 0%" with a buffered piecewise-power
    /// curve: stockpile fraction ≥ <c>bufferThreshold</c> (0.20) → full output; below it the
    /// output tapers along two convex branches stitched through the hard node
    /// (<c>anchorFrac</c>=0.05 → <c>anchorOutput</c>=0.30) down to <c>minOutputAtZero</c> (0) at
    /// empty. Both branches use a power &gt; 1 so the drop accelerates toward zero (CONCEPT §6).
    /// </summary>
    public static class FuelCurveLogic
    {
        /// <summary>Max value of vanilla <c>ResourceConsumer.m_ResourceAvailability</c> (byte) —
        /// divisor to turn it into a 0..1 stockpile fraction.</summary>
        public const float ResourceAvailabilityMax = 255f;

        /// <summary>
        /// Fuel output factor ∈ [minOutputAtZero, 1] from a stockpile fraction ∈ [0,1].
        /// Piecewise-power through the hard nodes (0→min), (anchorFrac→anchorOutput),
        /// (bufferThreshold→1). Called only for thermal plants; non-thermal pass fraction 1 → 1.
        /// </summary>
        public static float ComputeFuelFactor(
            float fuelFraction, bool enabled,
            float bufferThreshold, float minOutputAtZero,
            float anchorFrac, float anchorOutput,
            float steepnessLow, float steepnessHigh)
        {
            if (!enabled)
                return 1f;

            float s = math.clamp(fuelFraction, 0f, 1f);
            if (s >= bufferThreshold)
                return 1f;                                 // buffer forgives ordinary supply delays

            float min = minOutputAtZero;                   // 0.0 = vanilla b==0 ⇒ 0
            float aF = anchorFrac;                         // 0.05
            float aO = anchorOutput;                       // 0.30
            if (s <= 0f)
                return min;

            // Zero-guards on config-driven divisors: a misconfigured aF=0 or bufferThreshold<=aF
            // must not produce NaN/Inf. EPS keeps the curve finite; the nodes (0.05/0.20) are the
            // intended values, the guard only matters for degenerate config.
            const float EPS = 1e-6f;
            if (s < aF)
            {
                // lower branch: u ∈ (0,1], y = min + (aO-min)·u^pLow
                float u = s / math.max(aF, EPS);
                return min + (aO - min) * math.pow(u, steepnessLow);
            }

            // upper branch: v ∈ [0,1), y = aO + (1-aO)·v^pHigh
            float v = (s - aF) / math.max(bufferThreshold - aF, EPS);
            return aO + (1f - aO) * math.pow(v, steepnessHigh);
        }
    }
}
