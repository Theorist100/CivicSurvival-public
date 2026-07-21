namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One telecom broadcast coverage circle wire entry for the ThreatUI radar mental-view
    /// binding. Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns
    /// the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Distinct from the runtime carrier CivicSurvival.Core.Utils.RadarBroadcastDto; the
    /// producer maps the runtime struct onto it before WriteTo.
    /// </summary>
    public partial struct RadarBroadcastDto
    {
        public float X;
        public float Z;
        public float Range;
    }
}
