namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One radar interception marker for the ThreatUI radar binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct RadarInterceptionDto
    {
        public float X;
        public float Z;
        public float TimeAgo;
        public float Lifetime;
        public bool Success;
    }
}
