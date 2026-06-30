using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    public enum PlantKind
    {
        Unclassified = 0,
        Thermal = 1,
        Renewable = 2,
        EmergencyGenerator = 3,
        Outside = 4,
        Hydro = 5,
        Geothermal = 6,
        Garbage = 7
    }

    /// <summary>
    /// Pipeline-owned classification for entities with ElectricityProducer.
    /// </summary>
    public struct PowerPlantKind : IComponentData
    {
        public PlantKind Value;
    }
}
