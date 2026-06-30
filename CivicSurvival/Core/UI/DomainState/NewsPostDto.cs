namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One Herald news entry. Mirrors the wire shape declared in
    /// ui-dto.contract.yaml; ui-dto codegen owns the WriteTo partial in
    /// DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct NewsPostDto
    {
        public string PostId;
        public string Source;
        public string Title;
        public string Body;
        public string Mood;
        public long Timestamp;
        public string Category;
        public string Scope;
        public bool IsAiGenerated;
    }
}
