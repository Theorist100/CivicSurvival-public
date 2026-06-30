using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Power
{
    [OwnedByFeatureId(FeatureIds.EngineeringName)]
    public interface IPowerCapacitySnapshotReader
    {
        bool TryGetSnapshot(out PowerCapacitySnapshot snapshot);
        int DispatchableMW { get; }
        int Version { get; }
    }
}
