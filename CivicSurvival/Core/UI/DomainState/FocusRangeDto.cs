namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Min/Max forecast range for an Intel focus channel
    /// (Energy/Infra/Residential). Mirrors the wire shape declared in
    /// ui-dto.contract.yaml; ui-dto codegen owns the WriteTo partial in
    /// DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct FocusRangeDto
    {
        public int Min;
        public int Max;

        public FocusRangeDto(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }
}
