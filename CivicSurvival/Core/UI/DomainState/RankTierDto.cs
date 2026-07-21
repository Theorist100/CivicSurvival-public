namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One defense-ladder rank tier (name, minimum score, icon id). Tier
    /// names and thresholds come from BalanceConfig (Arena.RankThresholds).
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct RankTierDto
    {
        public string Name;
        public long MinScore;
        public string Icon;
    }
}
