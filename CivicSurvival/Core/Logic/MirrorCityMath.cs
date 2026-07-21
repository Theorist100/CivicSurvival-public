using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;
using Unity.Collections;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure, blittable folds over a mirror city's target list — the same discipline as
    /// <see cref="StrikeResolver"/> (no World / EntityManager / RNG / config; a pure function of its
    /// inputs, Burst-compatible over <see cref="Unity.Mathematics"/> intrinsics and a
    /// <see cref="NativeArray{T}"/> of blittable <see cref="EnemyTarget"/> elements).
    ///
    /// Two responsibilities, both derivations only — they do NOT mutate <c>EnemyState</c> or the
    /// buffer; the caller (<c>Domains.GridWarfare.Systems.MirrorCitySystem</c>) applies the results:
    ///   1. <see cref="RecomputeAxis"/> — collapse the live targets of one axis into that axis value
    ///      (the derived cache the three stability axes become in the mirror model).
    ///   2. <see cref="InterceptChanceForTarget"/> — DRAFT positional intercept chance for a strike
    ///      aimed at one target, from the AA sites that cover it.
    ///
    /// Taking a <see cref="NativeArray{T}"/> (not a span) so the caller can pass
    /// <c>buffer.AsNativeArray()</c> straight from the <see cref="EnemyTarget"/> buffer with no copy.
    /// </summary>
    public static class MirrorCityMath
    {
        /// <summary>
        /// Fold the targets of one <paramref name="axis"/> into its axis value:
        /// <c>sum of contributions</c>, clamped to <c>[floor, cap]</c>, where
        ///   - an OPERATIONAL non-reserve target (progress ≥ 1) contributes
        ///     <c>Contrib · saturate(Hp/MaxHp)</c> — sub-lethal strike damage moves the axis
        ///     immediately and the repair tick visibly restores it (suppress-or-it-recovers loop);
        ///   - a target still building/rebuilding (progress &lt; 1) contributes
        ///     <c>Contrib · saturate(RebuildProgress)</c> — its build STATE, not hp: a construction
        ///     site's hp is pinned to <c>MirrorCityConstructionHpFraction</c> as an implementation
        ///     detail of being cheap to re-hit, so folding hp there would double-penalize it. The
        ///     switch is continuous: completion sets hp back to MaxHp, so both factors read 1.
        ///     A destroyed target contributes 0 either way (death paths zero both hp and progress);
        ///   - the reserve target is indestructible and always contributes its full <c>Contrib</c>,
        ///     which is also taken as the axis <c>floor</c> (the axis can never fall below it);
        ///   - AA sites carry <c>Contrib = 0</c> and so drop out naturally;
        ///   - the cap is <c>baseCap - max(0, capCut)</c> (never below the floor), where
        ///     <paramref name="capCut"/> is the accumulated cut from destroyed key targets.
        /// </summary>
        public static float RecomputeAxis(
            NativeArray<EnemyTarget> targets,
            AttackCategory axis,
            float baseCap,
            float capCut)
        {
            float sum = 0f;
            float floor = 0f;

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Axis != axis)
                    continue;

                if (t.Tier == MirrorTargetTier.Reserve)
                {
                    // Indestructible: its full contribution is the axis floor and always counts.
                    floor += t.Contrib;
                    sum += t.Contrib;
                    continue;
                }

                // Contribution (see summary): operational share scales with remaining hp so strike
                // damage and repair are both visible in the axis; a building/rebuilding target
                // scales with its build progress instead (its hp is a pinned constant there).
                if (t.RebuildProgress >= 1f)
                    sum += t.Contrib * (t.MaxHp > 0f ? math.saturate(t.Hp / t.MaxHp) : 0f);
                else
                    sum += t.Contrib * math.saturate(t.RebuildProgress);
            }

            float cap = math.max(floor, baseCap - math.max(0f, capCut));
            return math.clamp(sum, floor, cap);
        }

        /// <summary>
        /// The structural floor of one <paramref name="axis"/> — the sum of its indestructible
        /// reserve contributions, i.e. the same lower clamp <see cref="RecomputeAxis"/> applies.
        /// Single source for "the axis physically cannot go lower": the arrival owner compares the
        /// post-strike axis against this (combined with the balance <c>PressureFloor</c> clamp the
        /// axis owner applies) to decide a floor-touch, instead of duplicating the floor definition.
        /// </summary>
        public static float AxisFloor(NativeArray<EnemyTarget> targets, AttackCategory axis)
        {
            float floor = 0f;
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Axis == axis && t.Tier == MirrorTargetTier.Reserve)
                    floor += t.Contrib;
            }
            return floor;
        }

        /// <summary>
        /// DRAFT positional intercept model: the probability [0..1] that a strike aimed at
        /// <paramref name="target"/> is shot down by the enemy's air defence.
        ///
        /// Each AA site (<see cref="MirrorTargetTier.AaSite"/>) that is alive (Hp &gt; 0) and whose
        /// <c>AaRange</c> covers the target's position contributes an independent
        /// <paramref name="perSiteChance"/>; the site chances combine as a probability union
        /// (<c>1 - Π(1 - perSiteChance)</c>), so stacking coverage helps with diminishing returns.
        /// A SEAD strike aimed AT an AA site is not defended by that same site
        /// (<paramref name="targetIndex"/> is excluded from the union) — otherwise every AA site
        /// would grant itself a guaranteed extra <paramref name="perSiteChance"/> of self-cover and
        /// suppressing air defence would be systematically harder than the coverage map shows; only
        /// the OTHER sites whose range overlaps defend it.
        /// The whole result is then scaled by <paramref name="ballisticMultiplier"/> — a value &lt; 1
        /// for ballistic strikes (harder to intercept, mirroring the player's Patriot-vs-PVO), 1 for
        /// drones. DRAFT: the union rule, the per-site value, and the ballistic scaling are all
        /// placeholder tuning to be calibrated / moved to balance in phase D.
        /// </summary>
        public static float InterceptChanceForTarget(
            in EnemyTarget target,
            int targetIndex,
            NativeArray<EnemyTarget> allTargets,
            float ballisticMultiplier,
            float perSiteChance)
        {
            float pass = 1f; // probability the strike is NOT intercepted by any site
            float siteChance = math.saturate(perSiteChance);

            for (int i = 0; i < allTargets.Length; i++)
            {
                if (i == targetIndex)
                    continue; // an AA site does not intercept the strike flying at itself (SEAD)

                var aa = allTargets[i];
                if (aa.Tier != MirrorTargetTier.AaSite)
                    continue;
                if (aa.Hp <= 0f)
                    continue;
                if (aa.AaRange <= 0f)
                    continue;

                float dx = aa.X - target.X;
                float dz = aa.Z - target.Z;
                float distSq = (dx * dx) + (dz * dz);
                if (distSq > aa.AaRange * aa.AaRange)
                    continue; // target outside this site's coverage

                pass *= 1f - siteChance;
            }

            float intercept = (1f - pass) * math.saturate(ballisticMultiplier);
            return math.saturate(intercept);
        }

        /// <summary>
        /// Positional intercept chance [0..1] for the strike aimed at the target at
        /// <paramref name="targetIndex"/> in <paramref name="targets"/> — the by-index entry point used by
        /// <c>MirrorCitySystem.ApplyStrikeToTarget</c> (phase C). Sums the coverage of every alive AA site
        /// (<see cref="MirrorTargetTier.AaSite"/>, <c>Hp &gt; 0</c>) whose <c>AaRange</c> reaches the target
        /// (distance ≤ range), each contributing <paramref name="baseChance"/> as a probability union, then
        /// scales by <paramref name="ballisticMultiplier"/> (&lt; 1 lowers intercept for a ballistic strike,
        /// 1 for a drone). Pure static; delegates to <see cref="InterceptChanceForTarget"/>. Returns 0 for an
        /// out-of-range index (no target → nothing to intercept).
        /// </summary>
        public static float EffectiveInterceptChance(
            NativeArray<EnemyTarget> targets,
            int targetIndex,
            float baseChance,
            float ballisticMultiplier)
        {
            if (targetIndex < 0 || targetIndex >= targets.Length)
                return 0f;

            return InterceptChanceForTarget(targets[targetIndex], targetIndex, targets, ballisticMultiplier, baseChance);
        }
    }
}
