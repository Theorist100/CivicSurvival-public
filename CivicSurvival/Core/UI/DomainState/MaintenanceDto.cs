using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Maintenance/procurement domain DTO.
    /// PendingOfferJson and ActiveContractsJson are pre-serialized JSON strings.
    /// </summary>
    public partial struct MaintenanceDto : IDomainDto
    {
        public string PendingProcurementOfferJson;
        public int ShadyContractCount;
        public int TotalContractCount;
        public string ActiveContractsJson;
        public string MaintenanceContractRequestJson;
    }
}
