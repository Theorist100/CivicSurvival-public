namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Type of casualty for shock calculation.
    /// Different building types have different shock multipliers.
    /// </summary>
    public enum CasualtyType : byte
    {
        Residential = 0,    // x1.0 shock
        Hospital = 1,       // x2.0 shock (Balance.Attention.MULT_HOSPITAL)
        School = 2,         // x2.0 shock (Balance.Attention.MULT_SCHOOL)
        CriticalInfra = 3   // x1.5 shock (Balance.Attention.MULT_CRITICAL_INFRA)
    }
}
