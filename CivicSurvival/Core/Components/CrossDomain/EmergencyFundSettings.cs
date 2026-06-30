using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Config-only component for Emergency Fund corruption scheme.
    /// Single Writer: CorruptionSchemeRequestSystem (UI config changes).
    /// Split from EmergencyFundSingleton to prevent ECB full-struct stomp.
    /// Created and repaired together with EmergencyFundSingleton on the same entity;
    /// use EmergencyFundSingleton.EnsureExists to recreate the pair.
    /// Named "Settings" to avoid conflict with BalanceConfig.EmergencyFundConfig.
    /// </summary>
    public struct EmergencyFundSettings : IComponentData
    {
        /// <summary>Withdrawal percentage set by player (0, 25, 50, 75, 100).</summary>
        public int WithdrawPercent;

        public static EmergencyFundSettings Default => new()
        {
            WithdrawPercent = 0
        };
    }
}
