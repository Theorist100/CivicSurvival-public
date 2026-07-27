namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One active contract entry for the maintenance UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct ActiveContractEntry
    {
        public int EntityIndex;
        public string BuildingName;
        public string ContractType;
        public string VendorName;
        public float Quality;
        public int KickbackAmount;
        public bool IsShady;
        public int DaysRemaining;
    }
}
