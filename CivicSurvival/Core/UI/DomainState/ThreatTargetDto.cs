namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One threat target wire entry for the ThreatUI targets binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Distinct from the runtime carrier CivicSurvival.Core.Utils.ThreatTargetDto
    /// which carries float3 Position plus the per-threat detail list; this
    /// wire DTO collapses the position to Vector3IntDto and drops the inner
    /// threats list (UI only needs the aggregated summary fields).
    /// </summary>
    public partial struct ThreatTargetDto
    {
        public int EntityIndex;
        public int EntityVersion;
        public string Name;
        public Vector3IntDto Position;
        public int ThreatCount;
        public int MinEtaSeconds;
    }
}
