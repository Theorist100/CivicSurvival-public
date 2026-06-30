namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the weekly arena leaderboard binding. Mirrors the wire
    /// shape declared in ui-dto.contract.yaml; ui-dto codegen owns the
    /// WriteTo partial in DomainDtoWriters.g.cs. WeekStart is intentionally
    /// omitted because the UI does not consume it. Distinct from the
    /// server transport type
    /// CivicSurvival.Contracts.Services.Arena.WeeklyLeaderboardEntry.
    /// </summary>
    public partial struct WeeklyLeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public int FloorHits;
        public long DamageDealt;
    }
}
