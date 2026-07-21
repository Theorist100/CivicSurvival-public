using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct ImportDto : IDomainDto
    {
        public int ShadowImportMW;
        public int MaxShadowImportMW;
        public int SelectedPresetIndex;
        public int ShadowImportCost;
        public float DiscoveryRisk;
        public int ShadowImportDaysActive;
        public bool IsSanctioned;
        public int ShadowImportSanctionDays;
        public ActionAvailabilityField ShadowImportAvailability;
        public bool IsFrozen;
        public int FreezeReason;
        public string ShadowTradeImportRequestJson;
    }
}
