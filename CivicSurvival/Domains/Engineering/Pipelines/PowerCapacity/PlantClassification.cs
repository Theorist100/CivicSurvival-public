using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Engineering.Pipelines.PowerCapacity
{
    internal readonly struct PlantClassification
    {
        public PlantClassification(
            PlantKind kind,
            int originalCapacityKW,
            PowerPlantUtils.PlantType plantType,
            CapacityChannel channel)
        {
            Kind = kind;
            OriginalCapacityKW = originalCapacityKW;
            PlantType = plantType;
            Channel = channel;
        }

        public readonly PlantKind Kind;
        public readonly int OriginalCapacityKW;
        public readonly PowerPlantUtils.PlantType PlantType;
        public readonly CapacityChannel Channel;
    }
}
