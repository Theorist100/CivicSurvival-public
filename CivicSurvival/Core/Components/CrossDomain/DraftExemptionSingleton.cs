using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// ECS Singleton for the Draft-Exemption corruption scheme state — selling
    /// draft deferrals and fake disability exemptions for Shadow Cash at the cost
    /// of the mobilization manpower pool.
    ///
    /// Two layers:
    /// - <see cref="RatePercent"/> — the deferral rate (0, 15, 30, 50). Reversible:
    ///   while the rate is held it removes a proportional slice of the pool and pays
    ///   a daily wage; dropping the rate returns the men. Owned via
    ///   <c>CorruptionSchemeRequest</c> (like Fuel Siphoning).
    /// - <see cref="DisabilityHeadcount"/> — accumulated permanent head-count sold as
    ///   fake disability via toast offers. Never returns (no reform on the first pass).
    ///
    /// Writer: DraftExemptionSystem (Corruption domain).
    /// Readers: MobilizationSystem (flat manpower deduction), UI.
    /// The manpower deduction lives in <see cref="Core.Logic.ManpowerLogic"/>; this
    /// component is the single persisted source of truth for both layers.
    /// </summary>
    public struct DraftExemptionSingleton : IComponentData
    {
        /// <summary>Deferral rate percentage (0, 15, 30, 50). Reversible slice of the manpower pool.</summary>
        public int RatePercent;

        /// <summary>Accumulated permanent head-count removed by sold disability exemptions.</summary>
        public int DisabilityHeadcount;

        /// <summary>Daily Shadow-Cash wage from the deferral rate.</summary>
        public readonly double DailyIncome =>
            RatePercent * BalanceConfig.Current.DraftExemption.RateIncomePerPercentDay;

        public void SetDefaults() => this = Default;

        /// <summary>Default state — no rate, no accumulated disability.</summary>
        public static DraftExemptionSingleton Default => new() { RatePercent = 0, DisabilityHeadcount = 0 };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
