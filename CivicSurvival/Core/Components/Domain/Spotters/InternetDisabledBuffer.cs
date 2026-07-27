using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Spotters
{
    /// <summary>
    /// Buffer element for districts with disabled internet.
    /// Attached to SpotterCountermeasuresState singleton entity.
    ///
    /// When internet is disabled for a district:
    /// - Spotters in that district are inactive
    /// - Happiness and commerce penalties apply
    ///
    /// Serialized via SpotterSystem.Serialize/Deserialize
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct InternetDisabledBuffer : IBufferElementData
    {
#pragma warning disable CIVIC262 // Session-local runtime district key; NOT persisted (persist rides DistrictKeyCodec via SpotterAggregateCodec)
        public int DistrictIndex;
#pragma warning restore CIVIC262
    }
}
