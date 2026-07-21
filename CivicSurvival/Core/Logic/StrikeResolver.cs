using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure, seeded resolution of one player counter-strike against a mirror-city target,
    /// shared by the PvE runtime (<c>Domains.GridWarfare.Systems.MirrorCitySystem.ApplyStrikeToTarget</c>
    /// at arrival) and the future PvP server-authoritative recompute (Wave3Arena Phase-40):
    /// "a strike dealing <c>damageToHp</c> against a target at <c>targetHp</c>, defended by a
    /// positional intercept probability — is it intercepted, and what are the target's new hit points?".
    ///
    /// The intercept roll draws no session RNG: the outcome is a pure function of its inputs, and the
    /// roll seed is frozen at LAUNCH (not drawn at arrival) — it rides the projectile's serialized
    /// <c>OutboundStrikePayload.Seed</c> through flight, so a counter-strike caught in flight by a
    /// save replays the SAME intercept verdict when it arrives after load, and a server fed the same
    /// launch seed recomputes the identical verdict for an offline defender. Living in
    /// <c>Core/Logic</c> (Axiom 5) keeps it one definition for both callers.
    ///
    /// Pure over blittable <c>float</c>/<c>uint</c>, side-effect-free, Burst-compatible (only
    /// <see cref="Unity.Mathematics"/> intrinsics). It does NOT mutate the target — it returns the
    /// computed outcome and the caller applies it, keeping the per-tier death rules and the derived
    /// axis recompute in their existing home.
    ///
    /// Float-determinism caveat (Phase-40): <see cref="Unity.Mathematics.Random.NextFloat()"/> is an
    /// integer-hash → float divide, bit-identical across platforms; the only float op here is the
    /// <c>&lt;</c> comparison and a <c>max</c> clamp, so the PvE path is deterministic as-is. A strict
    /// PvP lockstep that also recomputes <c>exp</c>/<c>pow</c>-based damage upstream still needs the
    /// "server recomputes" decision recorded for Phase-40 — that lives outside this resolver.
    /// </summary>
    public static class StrikeResolver
    {
        /// <summary>
        /// Resolve one arriving strike against a single target's hit points. Pure, blittable, seeded,
        /// launch-frozen seed, Burst-compatible — the intercept verdict is reproducible after a
        /// mid-flight save/load and on a server. <paramref name="damageToHp"/>
        /// is the strike's hp damage (the caller has already converted axis damage → hp and zeroed it for an
        /// invulnerable reserve target); <paramref name="targetHp"/> is the target's current hp;
        /// <paramref name="effectiveInterceptChance"/> is the positional AA intercept probability [0..1]
        /// (saturated here). On an intercept the target takes 0 damage (<see cref="TargetStrikeOutcome.NewHp"/>
        /// == <paramref name="targetHp"/>); otherwise its hp drops to <c>max(0, targetHp - damageToHp)</c>.
        /// </summary>
        public static TargetStrikeOutcome ResolveTargetStrike(
            float damageToHp,
            float targetHp,
            float effectiveInterceptChance,
            uint seed)
        {
            float chance = math.saturate(effectiveInterceptChance);

            // Deterministic intercept roll from the launch-frozen seed (identical to Resolve's rule): the
            // RNG rejects a 0 state, so force a set bit.
            var rng = new Random(seed | 1u);
            float roll = rng.NextFloat();
            bool intercepted = roll < chance;

            if (intercepted)
            {
                return new TargetStrikeOutcome
                {
                    Intercepted = true,
                    NewHp = targetHp,
                    AppliedDamage = 0f
                };
            }

            float newHp = math.max(0f, targetHp - math.max(0f, damageToHp));
            return new TargetStrikeOutcome
            {
                Intercepted = false,
                NewHp = newHp,
                AppliedDamage = targetHp - newHp
            };
        }
    }

    /// <summary>
    /// Outcome of <see cref="StrikeResolver.ResolveTargetStrike"/>: whether the enemy's positional air
    /// defence intercepted the strike and the target's resulting hit points. Pure data — the caller
    /// (<c>MirrorCitySystem.ApplyStrikeToTarget</c>) applies <see cref="NewHp"/> to the target, runs the
    /// per-tier death rules, and recomputes the derived axes. No behaviour.
    /// </summary>
    public struct TargetStrikeOutcome
    {
        /// <summary>True when the enemy's air defence intercepted the strike — 0 hp damage landed.</summary>
        public bool Intercepted;

        /// <summary>The target's hit points after the strike (== the input hp if intercepted).</summary>
        public float NewHp;

        /// <summary>Hit-point damage actually applied (0 if intercepted or the target was invulnerable).</summary>
        public float AppliedDamage;
    }

}
