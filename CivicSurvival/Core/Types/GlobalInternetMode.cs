namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Global internet mode for the city.
    /// Affects cognitive warfare infection/recovery rates and economic penalties.
    ///
    /// Strategic tradeoffs:
    /// - OPEN: Full freedom, economy thrives, but propaganda spreads unchecked
    /// - FIREWALL: Filtered traffic, balanced approach, moderate penalties
    /// - BLACKOUT: Total isolation, maximum recovery, severe economic damage
    /// </summary>
    public enum GlobalInternetMode : byte
    {
        /// <summary>
        /// Full internet access. Economy unaffected.
        /// Infection rate: 100% (propaganda spreads freely)
        /// Recovery rate: 0% (no protection while online)
        /// Economic penalty: None
        /// </summary>
        Open = 0,

        /// <summary>
        /// Filtered traffic (government firewall).
        /// Infection rate: 30% (most propaganda blocked)
        /// Recovery rate: 50% (partial isolation helps recovery)
        /// Economic penalty: -10% commerce (VPNs, workarounds, friction)
        /// </summary>
        Firewall = 1,

        /// <summary>
        /// Total internet blackout.
        /// Infection rate: 0% (no digital propaganda)
        /// Recovery rate: 100% (full isolation)
        /// Economic penalty: -25% commerce (e-commerce dead, remote work impossible)
        /// </summary>
        Blackout = 2
    }
}
