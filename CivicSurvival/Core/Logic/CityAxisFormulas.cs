using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure three-axis city-instability math shared by the runtime city read
    /// (<c>Domains.GridWarfare.Systems.CityStabilitySystem</c>) and the mirror-enemy /
    /// future PvP-snapshot read (Wave3Arena Phase-30). Each axis maps one category of
    /// harm to a 0..1 instability factor: <see cref="PhysicalInstability"/> (blackout %,
    /// destroyed buildings, fires), <see cref="DigitalInstability"/> (power deficit, grid
    /// stress), <see cref="SocialInstability"/> (happiness penalty). The weighted blend into
    /// a single stability score is <see cref="StabilityFromAxes"/>.
    ///
    /// Before this unit the three formulas lived only inside <c>CityStabilitySystem</c>, so a
    /// mirror-enemy or server-recompute (both outside the GridWarfare domain) had no seam to
    /// call them — and <c>Core → Domain</c> is banned (Axiom 5 / CIVIC179), so they could not
    /// reach into the domain. Pulling the rule here makes it one definition both the runtime
    /// city and the enemy/PvP snapshot call; CIVIC500 keeps a domain from re-inlining a copy.
    ///
    /// All functions are pure over blittable <c>float</c>/<c>int</c>, side-effect-free, and
    /// take their weights and caps as parameters — no <c>BalanceConfig</c> or ECS dependency
    /// (the caller reads its own config and passes the values in, mirroring
    /// <see cref="GridStressMath"/> / <see cref="WearMath"/>). Byte-identical to the prior
    /// inline forms: same <c>math.clamp</c> order, same divisor floors, same sub-weight
    /// multiplications. The caller decides which inputs are present — an absent data source
    /// is passed as the zero that produced the same skipped term before (a count of <c>0</c>
    /// clamps to a <c>0</c> contribution, identical to the prior conditional <c>+=</c>).
    /// </summary>
    public static class CityAxisFormulas
    {
        /// <summary>
        /// Physical-axis instability (0..1): blackout coverage, destroyed buildings, fires,
        /// each ratio'd against its cap, clamped to <c>[0,1]</c>, and weighted by its
        /// sub-weight, then summed.
        /// <list type="bullet">
        /// <item><c>clamp(affectedDistricts / max(totalDistricts, 1), 0, 1) · blackoutSubWeight</c></item>
        /// <item><c>clamp(buildingsDestroyed / max(maxDestroyedBuildings, 1), 0, 1) · destroyedSubWeight</c></item>
        /// <item><c>clamp(buildingsOnFire / max(maxFires, 1), 0, 1) · firesSubWeight</c></item>
        /// </list>
        /// The runtime city passes <paramref name="affectedDistricts"/> = 0 when its penalty
        /// source is unavailable, and the destroyed/fire counts = 0 when the damage-stats
        /// singleton is absent — each clamps to a zero contribution, identical to the prior
        /// conditional <c>total +=</c>. The denominator floors guard div-by-zero on a
        /// remote-config cap of 0.
        /// </summary>
        public static float PhysicalInstability(
            int affectedDistricts,
            int totalDistricts,
            int buildingsDestroyed,
            int maxDestroyedBuildings,
            int buildingsOnFire,
            int maxFires,
            float blackoutSubWeight,
            float destroyedSubWeight,
            float firesSubWeight)
        {
            float total = 0f;

            float blackoutRatio = (float)affectedDistricts / math.max(totalDistricts, 1);
            total += math.clamp(blackoutRatio, 0f, 1f) * blackoutSubWeight;

            float destroyedRatio = math.clamp((float)buildingsDestroyed / math.max(maxDestroyedBuildings, 1), 0f, 1f);
            float firesRatio = math.clamp((float)buildingsOnFire / math.max(maxFires, 1), 0f, 1f);
            total += destroyedRatio * destroyedSubWeight;
            total += firesRatio * firesSubWeight;

            return total;
        }

        /// <summary>
        /// Digital-axis instability (0..1): power deficit and grid stress, each weighted by
        /// its sub-weight.
        /// <list type="bullet">
        /// <item>deficit — only when <paramref name="balance"/> &lt; 0 and
        /// <paramref name="consumption"/> &gt; 0:
        /// <c>clamp(abs(balance) / consumption, 0, 1) · deficitSubWeight</c>; otherwise no
        /// deficit term (a non-negative balance or zero consumption contributes 0).</item>
        /// <item>stress — <c>stressFactor(status) · stressSubWeight</c>, where the status
        /// factor is 0 (Normal/Surplus) / 0.5 (Warning) / 1.0 (Critical).</item>
        /// </list>
        /// </summary>
        public static float DigitalInstability(
            int balance,
            int consumption,
            GridStatusType status,
            float deficitSubWeight,
            float stressSubWeight)
        {
            float total = 0f;

            // Power deficit - only if negative balance
            if (balance < 0 && consumption > 0)
            {
                float deficitRatio = math.clamp((float)math.abs(balance) / math.max(consumption, 1), 0f, 1f);
                total += deficitRatio * deficitSubWeight;
            }

            // Grid stress
            float stressFactor = status switch
            {
                GridStatusType.Normal => 0f,
                GridStatusType.Warning => 0.5f,
                GridStatusType.Critical => 1.0f,
                GridStatusType.Surplus => 0f,
                _ => 0f
            };
            total += stressFactor * stressSubWeight;

            return total;
        }

        /// <summary>
        /// Social-axis instability (0..1): the maximum happiness penalty as a fraction of its
        /// ceiling — <c>clamp(maxHappinessPenalty / max(maxHappinessPenaltyCeiling, 0.01), 0, 1)</c>.
        /// Commerce is folded into happiness, so this single ratio carries the whole social
        /// weight. The runtime city passes <paramref name="maxHappinessPenalty"/> = 0 when its
        /// penalty source is unavailable (neutral social penalties), yielding 0 instability.
        /// The 0.01 floor guards div-by-zero on a remote-config ceiling of 0.
        /// </summary>
        public static float SocialInstability(float maxHappinessPenalty, float maxHappinessPenaltyCeiling)
        {
            float maxPenalty = math.max(maxHappinessPenaltyCeiling, 0.01f);
            return math.clamp(maxHappinessPenalty / maxPenalty, 0f, 1f);
        }

        /// <summary>
        /// Blend the three axis-instability factors into a city stability score in
        /// <c>[0,1]</c> (1 = perfect, 0 = crisis): weighted-sum the instabilities, then
        /// invert and clamp — <c>clamp(1 - (physical·physicalWeight + digital·digitalWeight +
        /// social·socialWeight), 0, 1)</c>.
        /// </summary>
        public static float StabilityFromAxes(
            float physical, float digital, float social,
            float physicalWeight, float digitalWeight, float socialWeight)
        {
            float instability = physical * physicalWeight
                              + digital * digitalWeight
                              + social * socialWeight;
            return math.clamp(1f - instability, 0f, 1f);
        }
    }
}
