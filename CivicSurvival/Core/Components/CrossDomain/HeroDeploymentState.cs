using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Hero deployment singleton ("The Voice").
    ///
    /// Owned by HeroDeploymentSystem (separate from CognitiveStateSystem).
    /// Split out of CognitiveState so hero-deploy concerns do not write to the
    /// same singleton as the infection/recovery loop.
    ///
    /// HeroStatus drives EffectiveInfectionRate and EffectiveRecoveryRate, both
    /// of which now live in <see cref="CognitiveRates"/> because the formula
    /// crosses two singletons (CognitiveState + HeroDeploymentState).
    ///
    /// Writer: HeroDeploymentSystem
    /// Readers: CognitiveStateSystem (for effective rates), MentalHealthResolverSystem,
    ///          DonorConferenceSystem (Greta trust bonus), CognitiveUISystem (DTO),
    ///          ScenarioInspectorSystem (debug).
    /// </summary>
    public struct HeroDeploymentState : IComponentData
    {
        /// <summary>Current hero status.</summary>
        public HeroStatus HeroStatus;

        /// <summary>Cost to deploy hero (deducted from city budget).</summary>
        public int HeroDeployCost;

        /// <summary>Infection rate reduction when hero is deployed (0.5 = 50%).</summary>
        public float HeroInfectionReduction;

        /// <summary>Recovery rate bonus when hero is lecturing (0.5 = 50%).</summary>
        public float HeroRecoveryBonus;

        private const int FALLBACK_HERO_DEPLOY_COST = 50000;
        private const float FALLBACK_HERO_INFECTION_REDUCTION = 0.5f;
        private const float FALLBACK_HERO_RECOVERY_BONUS = 0.5f;

        public static HeroDeploymentState Default
        {
            get
            {
                var cfg = BalanceConfig.Current?.Cognitive;
                return new()
                {
                    HeroStatus = HeroStatus.Inactive,
                    HeroDeployCost = cfg?.HeroDeployCost ?? FALLBACK_HERO_DEPLOY_COST,
                    HeroInfectionReduction = cfg?.HeroInfectionReduction ?? FALLBACK_HERO_INFECTION_REDUCTION,
                    HeroRecoveryBonus = cfg?.HeroRecoveryBonus ?? FALLBACK_HERO_RECOVERY_BONUS
                };
            }
        }

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
