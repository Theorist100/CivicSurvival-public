using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Spotters
{
    /// <summary>
    /// Buffer element for evacuated spotters that may return.
    /// Attached to SpotterCountermeasuresState singleton entity.
    ///
    /// When evacuation is performed, there's a 20% chance
    /// the spotter returns after 30 game days.
    ///
    /// Serialized via SpotterSystem.Serialize/Deserialize
    /// R9-L21: No district info stored — evacuated spotters respawn at random building by design.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct EvacuatedReturnBuffer : IBufferElementData
    {
        /// <summary>Game time (in hours) when spotter returns.</summary>
        public double ReturnTime;
    }
}
