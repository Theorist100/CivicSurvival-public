using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Cross-domain singleton: interest rate modifier registry.
    /// Currently holds config-based rate only. Future producers (CreditRating,
    /// Crisis, Sanctions) can register multipliers without code changes elsewhere.
    ///
    /// Written by: CityDebtTrackingSystem
    /// Read by: BudgetDebtPaymentRequest (indirectly — system passes resolved rate)
    ///
    /// Eliminates: S6-07 (compound interest death spiral — ceiling enforcement).
    /// </summary>
    public struct InterestRateRegistry : IComponentData
    {
        /// <summary>Maximum interest rate — prevents compound interest death spiral (S6-07).</summary>
        public const float MAX_INTEREST_RATE = 0.20f;

        public RateModifiers Rate;

        public static class Source
        {
            public const byte Config = 0;
        }

        public static InterestRateRegistry Default => new()
        {
            Rate = RateModifiers.Create(floor: 0.0f, ceiling: MAX_INTEREST_RATE)
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
