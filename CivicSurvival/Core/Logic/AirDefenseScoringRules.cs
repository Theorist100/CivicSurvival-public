using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Single source of truth for AA engagement scoring. Keep this Burst-friendly:
    /// no managed services, config reads, logging, or allocations.
    /// </summary>
    internal static class AirDefenseScoringRules
    {
        public const float InvalidScore = -1f;

        private const float CriticalEngageBonus = 50f;
        private const float ResidentialScorePenalty = 80f;
        private const float ClearSkyBonus = 30f;
        private const float SecondaryPriority = 50f;
        private const float TertiaryPriority = 25f;

        public static float CalculateEngagementScore(
            float distanceToAA,
            float distanceToTarget,
            TargetCategory category,
            DefensePolicy policy,
            bool isOverResidential,
            float criticalDistance)
        {
            float score = 100f;
            score -= distanceToAA * 0.01f;
            score += GetCategoryPriorityBonus(category, policy);

            if (isOverResidential)
            {
                bool isCritical = distanceToTarget < criticalDistance;
                score += isCritical ? CriticalEngageBonus : -ResidentialScorePenalty;
            }
            else
            {
                score += ClearSkyBonus;
            }

            return math.max(0f, score);
        }

        public static bool IsInvalidScore(float score) => score < 0f;

        public static float GetCategoryPriorityBonus(TargetCategory category, DefensePolicy policy)
        {
            switch (policy)
            {
                case DefensePolicy.HumanitarianShield:
                    return category switch
                    {
                        TargetCategory.Critical => 100f,
                        TargetCategory.Energy => SecondaryPriority,
                        TargetCategory.Service => TertiaryPriority,
                        TargetCategory.Civilian => 0f,
                        _ => 0f
                    };
                case DefensePolicy.GridIntegrity:
                    return category switch
                    {
                        TargetCategory.Energy => 100f,
                        TargetCategory.Critical => SecondaryPriority,
                        TargetCategory.Service => TertiaryPriority,
                        TargetCategory.Civilian => 0f,
                        _ => 0f
                    };
                default:
                    return 0f;
            }
        }
    }
}
