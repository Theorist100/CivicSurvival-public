namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One air-defense coverage circle wire entry for the ThreatUI radar binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns
    /// the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Distinct from the runtime carrier CivicSurvival.Core.Utils.RadarDefenseDto;
    /// the producer maps the runtime struct onto it before WriteTo.
    /// </summary>
    public partial struct RadarDefenseDto
    {
        public float X;
        public float Z;
        public float Range;
    }
}
