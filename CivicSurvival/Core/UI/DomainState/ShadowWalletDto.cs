namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Shadow wallet snapshot for the Finance UI panel. Available is liquid
    /// minus pending deductions; LockedBalance includes pending; TotalAssets
    /// is the sum. ShadowIncome/Expenses are running ledger totals.
    /// </summary>
    public partial struct ShadowWalletDto
    {
        public long Available;
        public long LockedBalance;
        public long TotalAssets;
        public long ShadowIncome;
        public long ShadowExpenses;
    }
}
