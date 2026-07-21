using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// ECS Singleton for the Construction Kickback corruption scheme state.
    ///
    /// Mechanics:
    /// - Player sets kickback percentage (0%, 5%, 10%, 20%)
    /// - Every road/net construction spend accrues KickbackPercent of the paid
    ///   cost into PendingPayout (inflated invoices)
    /// - Once per day the accrued amount is paid out to the shadow wallet and
    ///   additionally deducted from the city budget (handled by ConstructionKickbackSystem)
    /// </summary>
    public struct ConstructionKickbackSingleton : IComponentData
    {
        /// <summary>Kickback percentage (0, 5, 10, 20).</summary>
        public int KickbackPercent;

        /// <summary>Accrued kickback amount awaiting the daily payout, $.</summary>
        public long PendingPayout;

        public void SetDefaults() => this = Default;

        /// <summary>Default state.</summary>
        public static ConstructionKickbackSingleton Default => new() { KickbackPercent = 0, PendingPayout = 0 };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
