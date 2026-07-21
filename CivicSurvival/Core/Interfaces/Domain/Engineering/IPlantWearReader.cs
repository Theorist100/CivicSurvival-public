using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Read-only access to power-plant wear state by stable plant id.
    /// Implemented by PlantWearSimulation (owner of the StablePlantId map),
    /// consumed by PlantRepairIntakeSystem and UI. Deliberately split from
    /// <see cref="IPlantRepairIntentReader"/> so the wear-state read surface
    /// owned by the simulation does not pull in the repair-intent surface
    /// owned by PlantRepairRequestProcessor — the split mirrors actual
    /// ownership boundaries and avoids a sim → processor dependency.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IPlantWearReader
    {
        /// <summary>
        /// Tuple return (out parameters are not supported by NullObjectGenerator
        /// — CIVIC420). Item1 is the success flag; Item2 carries the view when
        /// true, or default(PlantWearView) otherwise.
        /// </summary>
        (bool found, PlantWearView view) GetWearState(int stablePlantId);
    }
}
