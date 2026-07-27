namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the weekly prosperity-ladder (city quality-of-life board)
    /// binding. Mirrors the wire shape declared in ui-dto.contract.yaml;
    /// ui-dto codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct WeeklyProsperityLeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public long Score;
    }
}
