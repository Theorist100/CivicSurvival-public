using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct SchemesDto : IDomainDto
    {
        public int EmergencyFundWithdraw;
#pragma warning disable CIVIC167 // UI DTO mirrors game value; no accumulation here
        public double EmergencyFundBalance;
#pragma warning restore CIVIC167
        public int FuelSiphonPercent;
        public bool CorruptionWindowActive;
        public ActionAvailabilityField EmergencyFundAvailability;
        public ActionAvailabilityField FuelSiphonAvailability;
        public string CorruptionSchemeRequestJson;
    }
}
