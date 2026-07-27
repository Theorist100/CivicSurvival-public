using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure load-driven equipment-wear and explosion-risk math shared by the runtime
    /// wear job (<c>EquipmentWearJob</c>) and any future consumer (forecast per-plant
    /// timeline, server recompute). Before this unit the curves lived only as inline
    /// job code, so the forecast had no seam to call and silently omitted load-driven
    /// wear / explosion entirely. These functions are the extractable Core surface.
    ///
    /// All functions are pure over blittable <c>float</c>, side-effect-free, and
    /// Burst-compatible (only <see cref="Unity.Mathematics.math"/> intrinsics). They
    /// RETURN values; the caller owns its stores and applies the result. Byte-identical
    /// to the prior inline job code — same formulas, same operation order, same
    /// <c>math.*</c> calls.
    /// </summary>
    public static class WearMath
    {
        /// <summary>
        /// Load-driven wear rate (% per hour) for the current city load ratio.
        /// <list type="bullet">
        /// <item>load &gt; <paramref name="overloadThreshold"/> (overloaded):
        /// <c>baseWearRate · clamp(overloadMultiplier, 1, 10)</c> — the multiplier is
        /// clamped to a sane range to defend against remote-config corruption.</item>
        /// <item>load &gt; <paramref name="highLoadThreshold"/> (high load): <c>baseWearRate</c>.</item>
        /// <item>otherwise: <c>0</c> (no wear accrues).</item>
        /// </list>
        /// </summary>
        public static float WearRate(
            float loadRatio,
            float highLoadThreshold,
            float overloadThreshold,
            float baseWearRate,
            float overloadMultiplier)
        {
            if (loadRatio > overloadThreshold) // >100%
            {
                // Clamp remote config multiplier to sane range
                return baseWearRate * math.clamp(overloadMultiplier, 1f, 10f);
            }

            if (loadRatio > highLoadThreshold) // 90-100%
            {
                return baseWearRate;
            }

            return 0f;
        }

        /// <summary>
        /// New wear percent after accruing <paramref name="wearRate"/> over
        /// <paramref name="deltaHours"/>, capped at <paramref name="maxWearPercent"/>:
        /// <c>min(wearPercent + wearRate · deltaHours, maxWearPercent)</c>.
        /// </summary>
        public static float AccumulateWear(float wearPercent, float wearRate, float deltaHours, float maxWearPercent)
        {
            float newWear = wearPercent + wearRate * deltaHours;
            return math.min(newWear, maxWearPercent);
        }

        /// <summary>
        /// Linear explosion-risk-per-hour curve: <c>0</c> at <paramref name="dangerZoneThreshold"/> wear,
        /// rising to <paramref name="maxExplosionRisk"/> at <paramref name="maxWearPercent"/> wear:
        /// <c>((wear - dangerZoneThreshold) / max(0.001, maxWearPercent - dangerZoneThreshold)) · maxExplosionRisk</c>.
        /// The denominator floor guards div-by-zero when remote config sets
        /// <paramref name="maxWearPercent"/> == <paramref name="dangerZoneThreshold"/>.
        /// </summary>
        public static float ExplosionRiskPerHour(
            float wearPercent,
            float dangerZoneThreshold,
            float maxWearPercent,
            float maxExplosionRisk)
        {
            float wearAboveDanger = wearPercent - dangerZoneThreshold;
            // Guard against div-by-zero if remote config sets maxWearPercent == dangerZoneThreshold
            float dangerRange = math.max(0.001f, maxWearPercent - dangerZoneThreshold);
            return (wearAboveDanger / dangerRange) * maxExplosionRisk;
        }

        /// <summary>
        /// Per-tick explosion probability from a per-hour risk over <paramref name="deltaHours"/>:
        /// <c>saturate(1 - pow(saturate(1 - riskPerHour), deltaHours))</c>.
        /// </summary>
        public static float ExplosionChance(float riskPerHour, float deltaHours)
            => math.saturate(1f - math.pow(math.saturate(1f - riskPerHour), deltaHours));
    }
}
