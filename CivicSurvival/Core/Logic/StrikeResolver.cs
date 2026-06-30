using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure, seeded resolution of one player counter-strike against the enemy's defence,
    /// shared by the PvE runtime (<c>Domains.GridWarfare.Systems.EnemyOperationEffectSystem</c>
    /// at arrival) and the future PvP server-authoritative recompute (Wave3Arena Phase-40):
    /// "a strike of <see cref="AttackCategory"/>, axis reduction <c>damage</c>, against an enemy
    /// axis currently at <c>currentAxis</c> defended by <c>interceptChance</c> — is it intercepted,
    /// and what is the new axis value?".
    ///
    /// Before this unit the intercept roll and the axis-floor clamp lived inline inside
    /// <c>EnemyOperationEffectSystem.ApplyArrivalEffects</c>, and the roll drew from a session
    /// <c>Unity.Mathematics.Random</c> created per world — non-deterministic across a save/load and
    /// impossible for a server to reproduce. Pulling the rule into <c>Core/Logic</c> (Axiom 5: a
    /// server/sweep outside the GridWarfare domain cannot call a domain method, and Core → Domain is
    /// banned) makes it one definition both the runtime and the recompute call, and replacing the
    /// session RNG with an explicit <paramref name="seed"/> makes the outcome a pure function of its
    /// inputs: the same <c>(category, damage, currentAxis, axisFloor, interceptChance, seed)</c>
    /// yields the same <see cref="StrikeOutcome"/> every time, on every machine.
    ///
    /// The seed is frozen at LAUNCH (not drawn at arrival): it rides the projectile's serialized
    /// <c>OutboundStrikePayload.Seed</c> through flight, so a counter-strike caught in flight by a
    /// save replays the SAME intercept verdict when it arrives after load — and a server fed the same
    /// launch seed recomputes the identical verdict for an offline defender.
    ///
    /// Pure over blittable <c>float</c>/<c>uint</c>/<c>enum</c>, side-effect-free, Burst-compatible
    /// (only <see cref="Unity.Mathematics"/> intrinsics). It does NOT mutate <c>EnemyState</c> —
    /// it returns the computed outcome and the caller applies it (<c>EnemyState.ReduceAxis</c>),
    /// keeping the exactly-once commit and the Category→axis mapping in their existing home.
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
        /// Resolve a single arriving counter-strike. <paramref name="seed"/> is the launch-frozen
        /// seed carried on the projectile; <paramref name="interceptChance"/> is the enemy's defence
        /// probability [0..1] (saturated here, so an out-of-range config value is harmless);
        /// <paramref name="currentAxis"/> is the targeted axis's current value and
        /// <paramref name="axisFloor"/> the floor it cannot drop below. On an intercept the strike
        /// lands 0 axis damage (<see cref="StrikeOutcome.NewAxis"/> == <paramref name="currentAxis"/>);
        /// otherwise the axis drops to <c>max(axisFloor, currentAxis - damage)</c>.
        /// </summary>
        public static StrikeOutcome Resolve(
            AttackCategory category,
            float damage,
            float currentAxis,
            float axisFloor,
            float interceptChance,
            uint seed)
        {
            float chance = math.saturate(interceptChance);

            // Deterministic intercept roll: a pure function of the launch-frozen seed. Unity.Mathematics.Random
            // requires a non-zero seed (a 0 state never advances), so force a set bit — identical for the
            // runtime and a server given the same seed.
            var rng = new Random(seed | 1u);
            float roll = rng.NextFloat();
            bool intercepted = roll < chance;

            if (intercepted)
            {
                return new StrikeOutcome
                {
                    Category = category,
                    Intercepted = true,
                    OldAxis = currentAxis,
                    NewAxis = currentAxis,
                    AppliedDamage = 0f
                };
            }

            float newAxis = math.max(axisFloor, currentAxis - damage);
            return new StrikeOutcome
            {
                Category = category,
                Intercepted = false,
                OldAxis = currentAxis,
                NewAxis = newAxis,
                AppliedDamage = currentAxis - newAxis
            };
        }
    }

    /// <summary>
    /// Outcome of <see cref="StrikeResolver.Resolve"/>: whether the enemy intercepted the strike and
    /// the resulting axis values. The caller (<c>EnemyOperationEffectSystem</c>) applies
    /// <see cref="NewAxis"/> via <c>EnemyState.ReduceAxis</c> (so the Category→axis mapping and the
    /// exactly-once commit stay in the domain) and uses <see cref="OldAxis"/>/<see cref="NewAxis"/>
    /// for the <c>EnemyAxisChangedEvent</c>. Pure data — no behaviour.
    /// </summary>
    public struct StrikeOutcome
    {
        /// <summary>Which enemy axis the strike targets (Kinetic→Physical, Cyber→Digital, Psyops→Social).</summary>
        public AttackCategory Category;

        /// <summary>True when the enemy's defence intercepted the strike — 0 axis damage landed.</summary>
        public bool Intercepted;

        /// <summary>The targeted axis value before the strike.</summary>
        public float OldAxis;

        /// <summary>The targeted axis value after the strike (== <see cref="OldAxis"/> if intercepted).</summary>
        public float NewAxis;

        /// <summary>Axis reduction actually applied (<c>OldAxis - NewAxis</c>; 0 if intercepted or floored).</summary>
        public float AppliedDamage;
    }
}
