using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Read-only access to pending plant-repair intent state.
    /// Implemented by PlantRepairRequestProcessor, consumed by PowerGridUISystem
    /// (snapshot version observer) and PlantRepairIntakeSystem (duplicate-intent
    /// guard before intent creation). Split from the original IPlantWearReader so the
    /// drain-pipeline state surface is owned by the system that publishes it.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IPlantRepairIntentReader
    {
        [NullReturnNull]
        IVersionedView<PlantRepairIntentSnapshot>? RepairIntentView { get; }

        bool HasPendingRepairIntent(int stablePlantId);
    }
}
