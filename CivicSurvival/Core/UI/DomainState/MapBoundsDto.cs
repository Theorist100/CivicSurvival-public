namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Radar map bounds wire payload for the ThreatUI radar binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct MapBoundsDto
    {
        public float MinX;
        public float MaxX;
        public float MinZ;
        public float MaxZ;
    }
}
