namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure "which target gets hit this strike" math, shared by the runtime target
    /// selector (<c>Domains.Waves.Logic.ThreatTargetSelector</c>) and the severity-sweep
    /// damage forecast (<c>Core.Forecast.DamageForecast</c>). The rule — pick an index with
    /// probability proportional to its residual-nameplate MW weight — used to live only
    /// inside the runtime selector while the forecast picked uniformly, so a megacity with
    /// one dominant station had its damage under-estimated. One rule, one home, both call it.
    ///
    /// Pure, side-effect-free, blittable int math: it takes the precomputed integer weights,
    /// their sum, and an already-drawn integer <paramref name="roll"/> in <c>[0, totalWeight)</c>,
    /// and RETURNS the chosen position. The RNG stays in the caller (the runtime keeps its
    /// <c>SerializableRandom</c>, the forecast its <c>Unity.Mathematics.Random</c>), so neither
    /// owner's serialization or random sequence changes — only the selection arithmetic is shared.
    /// Burst-compatible (no managed types, no allocation; <see cref="System.ReadOnlySpan{T}"/> is
    /// blittable and works from a job).
    /// </summary>
    public static class DamageTargetingMath
    {
        /// <summary>
        /// Cumulative-weight walk: returns the first index <c>i</c> in <paramref name="weights"/>
        /// where <paramref name="roll"/> falls below the running sum of weights[0..i]. Caller
        /// passes <paramref name="totalWeight"/> = sum of (clamped non-negative) weights and a
        /// <paramref name="roll"/> drawn uniformly in <c>[0, totalWeight)</c>. Each weight is
        /// clamped to <c>&gt;= 0</c> here so a stray negative can never skew the sum vs. the walk.
        ///
        /// Returns 0 when there are no weights (the callers guard non-empty candidate lists, so
        /// index 0 is a valid target index in every reachable scenario). When
        /// <paramref name="totalWeight"/> is not positive the caller is expected to have drawn a
        /// uniform fallback pick instead of calling this (see <c>SelectWeightedIndex</c>); should
        /// it call anyway, the walk returns the last index — a defined, in-range result.
        /// </summary>
        public static int WeightedPick(System.ReadOnlySpan<int> weights, int totalWeight, int roll)
        {
            if (weights.Length == 0)
                return 0;

            int accumulated = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                accumulated += weights[i] < 0 ? 0 : weights[i];
                if (roll < accumulated)
                    return i;
            }

            // Unreachable when roll < totalWeight and totalWeight matches the walked sum;
            // returns an in-range index regardless of float-free rounding.
            return weights.Length - 1;
        }
    }
}
