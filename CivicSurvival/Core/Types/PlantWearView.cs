namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Read-only repair state for a tracked power plant.
    /// </summary>
    public readonly struct PlantWearView
    {
        public int StablePlantId { get; init; }
        public BuildingRef Building { get; init; }
        public float WearPercent { get; init; }
        public bool IsUnderRepair { get; init; }
        public float RepairEndHour { get; init; }
    }
}
