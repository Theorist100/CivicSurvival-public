namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the all-time shadow-ladder (corruption board) binding.
    /// Net = Earned − Confiscated is the ranking metric. Mirrors the wire
    /// shape declared in ui-dto.contract.yaml; ui-dto codegen owns the
    /// WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct ShadowLeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public long Earned;
        public long Confiscated;
        public long Net;
        public string RankTier;
    }
}
