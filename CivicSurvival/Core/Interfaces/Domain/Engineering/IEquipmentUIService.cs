using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Engineering
{
    /// <summary>
    /// Service for reading equipment wear data for UI display.
    /// Provides read-only access to power plant wear status.
    ///
    /// Implementor: EquipmentUISystem (Engineering domain)
    /// Consumer: PowerGridUIPanel (PowerGrid domain)
    /// Null-object: empty snapshot, PlantsVersion=0, Touch is no-op.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IEquipmentUIService
    {
        /// <summary>
        /// Get snapshot of all plants with wear data.
        /// Thread-safe: returns cached data.
        /// </summary>
        [NullReturnEmpty]
        IReadOnlyList<PlantWearProducerData> GetPlantsSnapshot();

        /// <summary>
        /// Monotonically increasing version counter — incremented whenever plant data changes.
        /// Consumers can skip re-serialization when version matches the last seen value.
        /// Thread-safe read; implementations may treat this as demand for fresh data.
        /// </summary>
        int PlantsVersion { get; }

        /// <summary>
        /// Marks equipment UI data as demanded without copying the snapshot.
        /// </summary>
        void Touch();

        /// <summary>
        /// Marks plant wear data dirty after a synchronous repair transaction.
        /// Implementations should make the next PlantsVersion/GetPlantsSnapshot read
        /// observe the fresh repair state even while GameSimulation is paused.
        /// The current implementation flips global dirty flags, so the previous
        /// per-plant parameter carried no signal — kept signatureless.
        /// </summary>
        void MarkPlantsDirty();
    }
}
