namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One static arena rank tier (name, minimum floor hits, icon id).
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct RankTierDto
    {
        public string Name;
        public int MinFloorHits;
        public string Icon;
    }
}
