namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One cognitive-warfare district entry for the CognitiveOps UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct CognitiveDistrictEntry
    {
        public int DistrictIndex;
        public string Name;
        public float Integrity;
        public bool HasInternet;
        public bool IsCompromised;
        public bool IsUnzoned;
    }
}
