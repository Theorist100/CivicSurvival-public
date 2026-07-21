namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Attack time estimate for the Intel panel. <see cref="MinHours"/> and
    /// <see cref="MaxHours"/> carry meaningful values only when
    /// <see cref="Status"/> is "available"; otherwise set them to -1 and the
    /// generated writer omits them via omitIfDefault.
    /// </summary>
    public partial struct AttackTimeEstimateDto
    {
        public string Status;
        public float MinHours;
        public float MaxHours;

        public static AttackTimeEstimateDto Unknown => new() { Status = "unknown", MinHours = -1f, MaxHours = -1f };

        public static AttackTimeEstimateDto Available(float minHours, float maxHours) => new()
        {
            Status = "available",
            MinHours = minHours,
            MaxHours = maxHours,
        };

        public static AttackTimeEstimateDto WithStatus(string status) => new() { Status = status, MinHours = -1f, MaxHours = -1f };
    }
}
