using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.AirDefense
{
    /// <summary>
    /// Read-only air-defense credit snapshot for cross-domain callers.
    /// Implemented by AirDefenseStateSystem so consumers do not read the
    /// AirDefense-owned singleton directly or force a job dependency sync.
    /// Null-object is unavailable, so one-way Defense aid fails closed when the
    /// AirDefense consumer is not registered.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAirDefenseCreditsReader
    {
        bool IsAvailable { get; }
        bool IsDonorPatriotCreditCapReached { get; }
    }
}
