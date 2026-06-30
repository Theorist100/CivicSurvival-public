namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Read-only repair state for a damaged civilian building.
    /// </summary>
    public readonly struct CivilianRepairView
    {
        public BuildingRef Building { get; init; }
        public int HitCount { get; init; }
        public bool IsUnderRepair { get; init; }
        public float RepairEndHour { get; init; }
    }
}
