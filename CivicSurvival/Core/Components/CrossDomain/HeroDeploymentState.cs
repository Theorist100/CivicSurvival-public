using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

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

        // ════════════════════════════════════════════════════════════════
        // Archetype + Arestovych trust-debt (gameplay-state, serialized)
        // ════════════════════════════════════════════════════════════════

        /// <summary>Selected speaker archetype. Default <see cref="HeroArchetype.Voice"/> (= original Gerda). Serialized.</summary>
        public HeroArchetype Archetype;

        /// <summary>
        /// Arestovych "trust debt" (0..ArestovychDebtMax — config-tunable, default 1) accruing while
        /// Arestovych is active. A qualifying major hit (FakeVideo land) while active collapses his
        /// instant-cut buff for longer the more debt accrued ("worse than before"). Serialized; the
        /// persist read is non-negative + NaN/Inf-guarded but uncapped, so a raised debtMax survives
        /// load (the runtime is the sole cap). Only meaningful for the Arestovych archetype.
        /// </summary>
        public float ArestovychTrustDebt;

        /// <summary>Absolute game-hour the debt-collapse window ends (0 = no collapse). Restored verbatim across load; expires against the live clock — no re-anchor.</summary>
        public float DebtCollapseEndHour;

        /// <summary>Absolute game-hour before which a new debt collapse cannot trigger (anti-spam, speed-locked). Restored verbatim across load; expires against the live clock — no re-anchor.</summary>
        public float DebtCooldownEndHour;

        /// <summary>Absolute game-hour before which the active hero's archetype cannot be hot-swapped again (switch friction). Restored verbatim across load; expires against the live clock — no re-anchor.</summary>
        public float ArchetypeSwitchCooldownEndHour;

        /// <summary>True while an Arestovych debt-collapse window is open at <paramref name="nowGameHour"/>
        /// (the exposed lie loses its bite — his instant-cut counter drops to the floor). DebtCollapseEndHour
        /// is only ever set for the Arestovych archetype (HeroDeploymentSystem.ApplyArestovychMajorHit),
        /// so the archetype check is belt-and-suspenders. Single source of the suppression predicate for
        /// all three consumers (infection resolver, coverage posture, UI).</summary>
        public readonly bool IsArestovychDebtCollapsing(float nowGameHour)
            => Archetype == HeroArchetype.Arestovych
               && DebtCollapseEndHour > 0f
               && nowGameHour < DebtCollapseEndHour;

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
                    HeroRecoveryBonus = cfg?.HeroRecoveryBonus ?? FALLBACK_HERO_RECOVERY_BONUS,
                    Archetype = HeroArchetype.Voice,
                    ArestovychTrustDebt = 0f,
                    DebtCollapseEndHour = 0f,
                    DebtCooldownEndHour = 0f,
                    ArchetypeSwitchCooldownEndHour = 0f
                };
            }
        }

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
