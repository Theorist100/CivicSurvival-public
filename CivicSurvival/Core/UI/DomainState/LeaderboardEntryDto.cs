namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the all-time defense-ladder leaderboard binding. Mirrors the
    /// wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns the
    /// WriteTo partial in DomainDtoWriters.g.cs. Distinct from the server
    /// transport type CivicSurvival.Contracts.Services.Arena.LeaderboardEntry,
    /// which carries the same fact across the HTTP boundary.
    /// </summary>
    public partial struct LeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public int WavesSurvived;
        public long Score;
        public string RankTier;
    }
}
