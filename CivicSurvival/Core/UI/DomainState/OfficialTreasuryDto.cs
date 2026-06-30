namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// City treasury snapshot for the Finance UI panel. Balance is current
    /// liquid, TotalIncome and TotalExpenses are running ledger totals.
    /// Wire shape is generated from ui-dto.contract.yaml; writer lives in
    /// DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct OfficialTreasuryDto
    {
        public long Balance;
        public long TotalIncome;
        public long TotalExpenses;
    }
}
