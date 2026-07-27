using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Buffer element storing power data for a single district.
    /// Used with DynamicBuffer on singleton entity.
    /// </summary>
    [InternalBufferCapacity(16)] // Most cities have < 16 districts
    public struct DistrictPowerEntry : IBufferElementData
    {
        /// <summary>
        /// District reference (typed Index+Version). Prevents recycled entity indices
        /// from matching stale district power rows after delete/create in the same process.
        /// </summary>
        public DistrictRef District;

        /// <summary>
        /// Power consumption data for this district.
        /// </summary>
        public DistrictPowerData Data;
    }

    /// <summary>
    /// Buffer element storing district entity reference.
    /// Used by UI panels to iterate districts without running EntityQuery.
    /// NOTE: Entity stored as DistrictRef to avoid vanilla orphan detection (homeless spike bug).
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct DistrictEntityEntry : IBufferElementData
    {
        public DistrictRef District;

        /// <summary>
        /// Reconstructs Entity from stored Index+Version.
        /// Always check EntityManager.Exists() before using.
        /// </summary>
        public Entity GetEntity() => District.ToEntity();
    }

    /// <summary>
    /// Singleton component marking the entity that holds district power buffers.
    /// </summary>
    public struct DistrictPowerBufferSingleton : IComponentData
    {
        /// <summary>
        /// Total number of districts with power data.
        /// </summary>
        public int DistrictCount;

        /// <summary>
        /// Frame when data was last updated.
        /// </summary>
        [CivicSurvival.Core.Attributes.SimulationFrameStamp("Simulation frame stamp stored in ECS data; consumers compare it as a frame cursor, not a snapshot generation.")]
        public int LastUpdateFrame;

        /// <summary>
        /// City-wide total demand (kW) from ALL electricity consumers.
        /// Calculated in same pass as district data to guarantee consistency.
        /// </summary>
        public int TotalDemandKW;
    }
}
