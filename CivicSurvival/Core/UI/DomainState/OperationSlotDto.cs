namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One active grid-warfare operation slot for the GridWarfare UI panel.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct OperationSlotDto
    {
        public string AttackType;
        public string OperationState;
        public long Cost;
        public float Progress;
    }
}
