using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Population-tier classification (village / town / city). The single home for the
    /// population-bucketing rule that the Attention exodus multiplier and the Refugees
    /// spawn rate both key off. Both used to re-derive the same
    /// <c>population &lt; VillageMaxPop … &lt; TownMaxPop</c> thresholds inline; they now call
    /// <see cref="ClassifyPopulationTier"/> so the bucketing cannot drift between them.
    /// Each domain still maps the tier to its own per-tier values (exodus multiplier vs.
    /// refugee rate) — the shared thing is the classification, not the values.
    /// </summary>
    public enum PopulationTier
    {
        /// <summary>Unset sentinel — <c>ClassifyPopulationTier</c> never returns this; present so
        /// <c>default(PopulationTier)</c> does not silently mean Village (CIVIC020).</summary>
        Unknown = 0,
        /// <summary>population &lt; VillageMaxPop (autonomous — wells, stoves, cellars).</summary>
        Village,
        /// <summary>VillageMaxPop ≤ population &lt; TownMaxPop.</summary>
        Town,
        /// <summary>population ≥ TownMaxPop (infrastructure-dependent "concrete trap").</summary>
        City
    }

    /// <summary>
    /// Pure static functions for population-stability rules (exodus / refugee scaling).
    /// No side effects, no ECS or service dependencies. Constants live in
    /// <c>BalanceConfig.Current.Scenario</c> / <c>.Attention</c> for cross-domain access and
    /// server sync.
    /// </summary>
    public static class StabilityMath
    {
        /// <summary>
        /// Classify a population into its city-size tier using the scenario thresholds.
        /// This is the single bucketing rule shared by Attention (exodus city-size
        /// multiplier) and Refugees (spawn rate per tier). Takes a float so an integer
        /// citizen count widens losslessly and a cached float count compares identically
        /// to the previous inline <c>population &lt; VillageMaxPop</c> / <c>&lt; TownMaxPop</c>.
        /// </summary>
        public static PopulationTier ClassifyPopulationTier(float population)
        {
            var scenario = BalanceConfig.Current.Scenario;
            if (population < scenario.VillageMaxPop)   // < 1,000
                return PopulationTier.Village;
            if (population < scenario.TownMaxPop)      // 1,000-10,000
                return PopulationTier.Town;
            return PopulationTier.City;
        }

        /// <summary>
        /// City-size exodus multiplier ("Autonomy Coefficient"): infrastructure dependency
        /// determines exodus speed. Villages are autonomous (people stay); cities are
        /// concrete traps without power (people flee). Inverts the intuitive "big city has
        /// more inertia" because high-rises become death traps in infrastructure warfare.
        /// Negative configured multipliers are clamped to zero.
        /// </summary>
        public static float CitySizeExodusMultiplier(int population)
        {
            var scenario = BalanceConfig.Current.Scenario;
            switch (ClassifyPopulationTier(population))
            {
                case PopulationTier.Village:
                    return math.max(0f, scenario.ExodusMultiplierVillage);   // autonomous, stay
                case PopulationTier.Town:
                    return math.max(0f, scenario.ExodusMultiplierTown);      // baseline
                default:
                    return math.max(0f, scenario.ExodusMultiplierCity);      // concrete trap, flee
            }
        }

        /// <summary>
        /// Cognitive-integrity exodus multiplier ("Hero City Coefficient"): mental
        /// resilience determines exodus speed during a crisis. High integrity ("Kyiv 2022")
        /// keeps people in place; low integrity is panic / stampede. The integrity value is
        /// read by the caller (Attention reads Cognitive's published buffer average) and
        /// passed in — the <em>rule</em> (threshold → multiplier) lives here so it is not a
        /// cross-domain decision baked inside Attention. Thresholds aligned with the
        /// economics integrity gradient (80/50/30/10%).
        /// </summary>
        /// <param name="avgIntegrity">Average cognitive integrity (0..1); 1.0 = full.</param>
        public static float IntegrityExodusMultiplier(float avgIntegrity)
        {
            var attn = BalanceConfig.Current.Attention;
            if (avgIntegrity >= attn.IntegrityThresholdLoyal)       return attn.IntegrityMultLoyal;       // HEROIC
            if (avgIntegrity >= attn.IntegrityThresholdAnxious)     return attn.IntegrityMultAnxious;     // STABLE
            if (avgIntegrity >= attn.IntegrityThresholdRebellious)  return attn.IntegrityMultRebellious;  // ANXIOUS
            if (avgIntegrity >= attn.IntegrityThresholdBrainwashed) return attn.IntegrityMultBrainwashed; // BRAINWASHED
            return attn.IntegrityMultZombie;                                                              // ZOMBIE
        }

        /// <summary>
        /// Hero-archetype exodus multiplier. The Patriot "to the bitter end" mobilizer
        /// holds resistance but its fatalism ("stand and die") makes people flee faster — its
        /// signature debuff. Other archetypes are neutral (1×). The rule lives here in Core so the
        /// Attention exodus pull (which reads the cross-domain <c>HeroDeploymentState</c>) does not
        /// bake a Cognitive balance decision inside Attention (Axiom 5), mirroring
        /// <see cref="IntegrityExodusMultiplier"/>.
        /// </summary>
        public static float PatriotExodusMultiplier(HeroArchetype archetype)
        {
            if (archetype != HeroArchetype.Patriot)
                return 1.0f;
            return math.max(0f, BalanceConfig.Current.Cognitive.PatriotExodusMultiplier);
        }

        // Guards the sway-excess denominator when the threshold is configured at (or above) 1.0.
        private const float PsyThresholdHeadroomEps = 1e-4f;

        /// <summary>
        /// Poor-stratum psy-war exodus driver ("info-war bleed"). Turns the discrete psy-war into a
        /// first-class <em>cause</em> of exodus: the enemy's penetration of the poor
        /// (<paramref name="poorInfection"/>), net of the city's broadcast defence
        /// (<paramref name="poorCoverageFraction"/>), weighted by the poor's demographic share, yields a
        /// %/day rate that is <b>added to the kinetic (WorldShock) exodus base</b> before the registry
        /// resolves. This additive expression is the only one under which the psy-war can drive exodus
        /// alone — the exodus registry is multiplicative-only, so a psy factor on a zero-shock base stays
        /// zero. Poor-only by design (Middle/Wealthy psy harm routes to commerce / capital flight, not
        /// exodus). Mirrors <see cref="IntegrityExodusMultiplier"/> / <see cref="PatriotExodusMultiplier"/>:
        /// Attention reads the per-stratum psy posture from a Core interface and delegates the
        /// threshold→rate decision here, so no Cognitive balance choice is baked inside Attention (Axiom 5).
        ///
        /// Curve (linear v1): below <c>PsyExodusInfectionThreshold</c> → <c>0</c> (no bleed until the enemy
        /// actually swings the poor); fully-covered poor → <c>0</c> (defence holds); scales with the poor
        /// share so a mostly-poor city bleeds far faster than a mostly-wealthy one at the same infection.
        /// Defence <em>subtracts</em> from the attack (<c>1 − coverage</c>), never multiplies it.
        /// </summary>
        /// <param name="poorInfection">Poor average infection [0..1] — the enemy-sway proxy.</param>
        /// <param name="poorCoverageFraction">Fraction of poor households inside broadcast coverage [0..1].</param>
        /// <param name="poorHouseholds">Poor household count (the demographic weight numerator).</param>
        /// <param name="totalHouseholds">City-wide household count across all strata (the denominator).</param>
        /// <returns>%/day exodus rate contributed by the poor psy-war (additive base, ≥ 0).</returns>
        public static float PoorPsyExodusRatePercentPerDay(float poorInfection, float poorCoverageFraction, float poorHouseholds, float totalHouseholds)
        {
            var attn = BalanceConfig.Current.Attention;
            float threshold = attn.PsyExodusInfectionThreshold;
            float swayExcess = math.saturate((poorInfection - threshold) / math.max(PsyThresholdHeadroomEps, 1f - threshold));
            float uncovered = 1f - math.saturate(poorCoverageFraction);
            float poorShare = totalHouseholds > 0f ? poorHouseholds / totalHouseholds : 0f;
            return math.max(0f, attn.PsyExodusMaxRatePerDay * swayExcess * uncovered * poorShare);
        }

        /// <summary>
        /// Wealthy-stratum psy-war capital-flight driver ("Batalion Monako", Phase 16). The wealthy
        /// sibling of <see cref="PoorPsyExodusRatePercentPerDay"/>: the enemy's penetration of the
        /// wealthy (<paramref name="wealthyInfection"/>), net of the broadcast defence
        /// (<paramref name="wealthyCoverageFraction"/>), yields a %/day rate — but applied
        /// <b>directly to the wealthy subpopulation</b>, not weighted by its city share. Attention marks
        /// exactly that fraction of swayed wealthy households <c>MovingAway</c> as a separate targeted
        /// stream (the "N families fled" tell), so the rate is per-stratum, not per-city; there is no
        /// share factor. Same curve family as the poor rule: below
        /// <c>WealthyFlightInfectionThreshold</c> → <c>0</c> (no flight until the enemy actually swings
        /// the rich); fully-covered wealthy → <c>0</c> (defence holds); defence <em>subtracts</em>
        /// (<c>1 − coverage</c>), never multiplies. The threshold sits HIGHER than the Phase-08 bank-run
        /// (<c>BankRunDisloyaltyThreshold</c> = 0.4) so the banks run first (panic) and the families
        /// emigrate later (commitment) — an escalation ladder. Delegated here (Core) so Attention bakes
        /// no Cognitive balance decision (Axiom 5), exactly like the poor rule.
        /// </summary>
        /// <param name="wealthyInfection">Wealthy average infection [0..1] — the enemy-sway proxy.</param>
        /// <param name="wealthyCoverageFraction">Fraction of wealthy households inside broadcast coverage [0..1].</param>
        /// <returns>%/day flight rate OF THE WEALTHY STRATUM (≥ 0).</returns>
        public static float WealthyPsyFlightRatePercentPerDay(float wealthyInfection, float wealthyCoverageFraction)
        {
            var attn = BalanceConfig.Current.Attention;
            float threshold = attn.WealthyFlightInfectionThreshold;
            float swayExcess = math.saturate((wealthyInfection - threshold) / math.max(PsyThresholdHeadroomEps, 1f - threshold));
            float uncovered = 1f - math.saturate(wealthyCoverageFraction);
            return math.max(0f, attn.WealthyFlightMaxRatePerDay * swayExcess * uncovered);
        }
    }
}
