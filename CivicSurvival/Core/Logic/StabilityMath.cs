using Unity.Mathematics;
using CivicSurvival.Core.Config;

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
    }
}
