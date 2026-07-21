using System.Collections.Generic;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Finance domain DTO. Monetary values sent as JSON numbers (long).
    /// OfficialTreasury and ShadowWallet are nested subtypes; the per-category
    /// expense / income / debt maps are typed Dictionaries that the generated
    /// writer emits as JSON objects.
    /// </summary>
    public partial struct FinanceDto : IDomainDto
    {
        public long CityTreasury;
        public long TotalLiquidity;
        public OfficialTreasuryDto OfficialTreasury;
        public ShadowWalletDto ShadowWallet;
        public IReadOnlyDictionary<string, long>? Expenses;
        public IReadOnlyDictionary<string, long>? Income;
        public long TotalExpenses;
        public long TotalIncome;
        public long TotalDebt;
        public IReadOnlyDictionary<string, long>? DebtBreakdown;
        public bool DebtWarning;
        public bool DebtRestructured;
        /// <summary>FIX T3-12: Black market sanctions markup (0 = none, 1.5 = +150%).</summary>
        public float SanctionsMarkup;
    }
}
