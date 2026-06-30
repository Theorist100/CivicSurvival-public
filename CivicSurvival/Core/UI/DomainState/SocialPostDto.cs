namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One social-feed (Chipper) entry. Mirrors the wire shape declared in
    /// ui-dto.contract.yaml; ui-dto codegen owns the WriteTo partial in
    /// DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct SocialPostDto
    {
        public string Author;
        public string AuthorName;
        public string Message;
        public string Mood;
        public long Timestamp;
        public bool IsOfficial;
    }
}
