using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Wave leak regulator: holds the share of a wave that air defense kills near
    /// <c>Waves.MaxWaveInterceptFraction</c> by suppressing intercept chance, without ever
    /// making a threat unkillable.
    ///
    /// Replaces the per-target leak floor (a deterministic hash over the target building index
    /// that force-leaked a fixed slice of buildings). That floor had two failure modes players
    /// hit directly: the verdict was a constant per building, so a building in the leaking slice
    /// could never be defended no matter how much AA surrounded it; and the verdict landed after
    /// a kill was already rolled, so the kill had to be rolled back and the threat hidden from
    /// targeting — a full battery going silent over a drone flying straight over it.
    ///
    /// The regulator instead reads how the wave is actually going (kills vs impacts so far) and
    /// scales the next intercept roll. Suppression is proportional to how far the wave runs over
    /// its target share, bounded below by <paramref name="minMultiplier"/>, so every threat keeps
    /// a real chance of dying and no target is promised away in advance.
    ///
    /// Share is measured against threats already resolved this wave — NOT against the wave's full
    /// size. Measuring against wave size would spend the kill allowance early and clamp hard at
    /// the end, concentrating every breach into the wave's tail; measuring against resolved
    /// threats makes the regulator release as soon as the share drops, so breaches spread evenly.
    ///
    /// "Resolved" counts only what air defense answered for: kills, plus hits that were inside
    /// some ready AA's umbrella. A threat that flew outside every umbrella is not evidence about
    /// how well the guns are shooting, and letting it into the denominator would mean an
    /// undefended corner keeps the regulator permanently asleep over the defended core.
    ///
    /// Pure: no ECS, no config read (caller passes the tuning). Only multiply/divide/compare —
    /// no transcendental functions — so the verdict is stable for a future server-authoritative
    /// PvP re-evaluation.
    /// </summary>
    internal static class LeakRegulatorLogic
    {
        /// <summary>
        /// Multiplier applied to the intercept chance of the next roll, in [minMultiplier, 1].
        /// </summary>
        /// <param name="intercepted">Threats air defense killed so far this wave.</param>
        /// <param name="impacted">
        /// Threats that reached their target so far this wave AND were air defense's
        /// responsibility — inside some ready AA's candidate set at least once
        /// (<c>ThreatOutcomeStatsSingleton.CoveredHitsCount</c>). Hits from outside every
        /// umbrella must NOT be passed here: they would depress the measured share and switch
        /// the regulator off over the defended core, making coverage gaps a reward.
        /// </param>
        /// <param name="targetKillShare">Set point — <c>Waves.MaxWaveInterceptFraction</c>.</param>
        /// <param name="strength">
        /// How hard to suppress per unit of overshoot (<c>Waves.LeakRegulatorStrength</c>).
        /// 0 disables the regulator.
        /// </param>
        /// <param name="minMultiplier">
        /// Lower bound of the multiplier (<c>Waves.LeakRegulatorMinMultiplier</c>). This is what
        /// keeps every threat killable — at 0 the regulator degenerates back into the old floor's
        /// unkillable targets.
        /// </param>
        /// <param name="smoothing">
        /// Virtual prior samples at the set point (<c>Waves.LeakRegulatorSmoothing</c>), damping
        /// the estimate while the wave has resolved only a handful of threats. Without it the
        /// first kill reads as a 100% kill share and slams the multiplier to its floor.
        /// </param>
        public static float SuppressionMultiplier(
            int intercepted,
            int impacted,
            float targetKillShare,
            float strength,
            float minMultiplier,
            int smoothing)
        {
            float target = math.clamp(targetKillShare, 0f, 1f);
            // No ceiling to hold (target >= 1) or regulator switched off — full chance.
            if (target >= 1f || strength <= 0f)
                return 1f;

            float minMult = math.clamp(minMultiplier, 0f, 1f);
            int kills = math.max(0, intercepted);
            int hits = math.max(0, impacted);
            float k = math.max(0, smoothing);

            float resolved = kills + hits + k;
            if (resolved <= 0f)
                return 1f;

            // Prior-damped kill share: the k virtual samples sit exactly at the set point, so an
            // empty wave starts at zero overshoot and the multiplier opens at 1.
            float killShare = (kills + k * target) / resolved;
            float overshoot = math.max(0f, killShare - target);

            return math.clamp(1f - overshoot * strength, minMult, 1f);
        }
    }
}
