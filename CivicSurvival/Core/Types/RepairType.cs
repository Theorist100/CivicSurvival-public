namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Infrastructure repair types - two-lane repair system.
    /// Municipal Contract: slow (24h), City Budget, optional kickback.
    /// Shadow Ops: fast (2h), Shadow Cash, no kickback.
    /// </summary>
    public enum RepairType : byte
    {
        /// <summary>
        /// Municipal Contract - standard bureaucratic repair.
        /// 24 hours, paid from City Budget.
        /// </summary>
        Municipal = 0,

        /// <summary>
        /// Municipal Contract with Kickback.
        /// 24 hours, 2x cost from City Budget, 10% to Shadow Wallet.
        /// Increases corruption exposure.
        /// </summary>
        MunicipalWithKickback = 1,

        /// <summary>
        /// Shadow Ops - off-the-books repair.
        /// 2 hours, paid from Shadow Cash.
        /// No corruption exposure, but uses scarce resource.
        /// </summary>
        ShadowOps = 2,
    }
}
