namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One row of the weekly shadow-ladder (corruption board) binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct WeeklyShadowLeaderboardEntryDto
    {
        public int Position;
        public string Nickname;
        public long Net;
    }
}
