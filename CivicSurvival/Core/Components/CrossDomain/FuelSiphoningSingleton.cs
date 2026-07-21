using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// ECS Singleton for Fuel Siphoning corruption scheme state.
    /// Replaces IFuelSiphoningService for state storage.
    ///
    /// Mechanics:
    /// - Player sets siphon percentage (0%, 15%, 30%, 50%)
    /// - Higher siphon = generators consume fuel faster
    /// - Daily income added to offshore account (handled by FuelSiphoningSystem)
    /// </summary>
    public struct FuelSiphoningSingleton : IComponentData
    {
        /// <summary>Siphoning percentage (0, 15, 30, 50).</summary>
        public int SiphonPercent;

        /// <summary>
        /// Fuel consumption multiplier applied to generators.
        /// 0% = 1.0x, 15% = 1.3x, 30% = 1.6x, 50% = 2.0x
        /// </summary>
        public readonly float ConsumptionMultiplier =>
            1f + SiphonPercent * BalanceConfig.Current.FuelSiphoning.ConsumptionMultPerPercent;

        /// <summary>Daily income from siphoning.</summary>
        public readonly double DailyIncome =>
            SiphonPercent * BalanceConfig.Current.FuelSiphoning.IncomePerPercentDay;

        public void SetDefaults() => this = Default;

        /// <summary>Default state.</summary>
        public static FuelSiphoningSingleton Default => new() { SiphonPercent = 0 };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}


