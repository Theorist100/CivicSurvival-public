namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the all-time prosperity-ladder (city quality-of-life board)
    /// binding. Score = per-wave prosperity index (average happiness scaled by
    /// the housed share) summed over waves. Mirrors the wire shape declared in
    /// ui-dto.contract.yaml; ui-dto codegen owns the WriteTo partial in
    /// DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct ProsperityLeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public int Waves;
        public int AvgIndex;
        public long Score;
        public string RankTier;
    }
}
