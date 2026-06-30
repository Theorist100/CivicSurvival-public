
namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct ExportDto : IDomainDto
    {
        public int ExportPercent;
        public int ExportedMW;
        public int DailyIncome;
#pragma warning disable CIVIC167 // UI DTO mirrors game value; no accumulation here
        public double OffshoreBalance;
#pragma warning restore CIVIC167
        public bool IsFrozen;
        public int FreezeReason;
        public ActionAvailabilityField ExportAvailability;
        public string ShadowTradeExportRequestJson;
    }
}
