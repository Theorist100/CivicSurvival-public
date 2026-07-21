namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One radar target wire entry for the ThreatUI radar binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Distinct from the runtime carrier CivicSurvival.Core.Utils.RadarTargetDto
    /// which uses Core.Types.EntityRef (camelCase wire) and floats for
    /// internal radar state. This DTO is the wire boundary; the producer
    /// maps the runtime struct onto it before WriteTo.
    /// </summary>
    public partial struct RadarTargetDto
    {
        public EntityRefDto Entity;
        public float X;
        public float Z;
        public string Name;
        public float SizeX;
        public float SizeY;
        public float SizeZ;
        public float RotationY;
    }
}
