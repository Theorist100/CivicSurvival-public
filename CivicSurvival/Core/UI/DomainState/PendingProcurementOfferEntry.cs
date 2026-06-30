namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One pending procurement offer entry for the maintenance UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct PendingProcurementOfferEntry
    {
        public int EntityIndex;
        public int EntityVersion;
        public string Service;
        public string ContractType;
        public string OfficialVendorName;
        public string ShadyVendorName;
        public int OfficialPrice;
        public int ShadyPrice;
        public int KickbackOffer;
        public float OfficialQuality;
        public float ShadyQuality;
        public bool CanAcceptShady;
        public string AcceptShadyLockedReasonId;
        public int AcceptShadyEffectiveCost;
        public string BuildingName;
    }
}
