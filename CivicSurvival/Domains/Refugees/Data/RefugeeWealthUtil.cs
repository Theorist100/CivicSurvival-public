using Game.Prefabs;
using Unity.Mathematics;

namespace CivicSurvival.Domains.Refugees.Data
{
    /// <summary>
    /// Wealth floor for refugee households: the lower bound of the vanilla
    /// "Modest" wealth class (CitizenHappinessParameterData.m_WealthyMoneyAmount.y —
    /// see CitizenUIUtils.GetHouseholdWealthKey) scaled by
    /// Scenario.RefugeeWealthFloorMultiplier. Resolved from the vanilla singleton
    /// at call time, so a game rebalance keeps refugees middle-class without a
    /// config change. Multiplier 0 disables the floor (wallets only kept out of
    /// the negative by RefugeeRetentionSystem's aid pass).
    /// </summary>
    public static class RefugeeWealthUtil
    {
        public static int GetWealthFloor(in CitizenHappinessParameterData happinessParams, float multiplier)
        {
            return (int)math.round(happinessParams.m_WealthyMoneyAmount.y * math.max(0f, multiplier));
        }
    }
}
