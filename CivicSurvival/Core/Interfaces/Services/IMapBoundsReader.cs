using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types.Snapshots;

namespace CivicSurvival.Core.Interfaces.Services
{
    [InfrastructureService]
    public interface IMapBoundsReader : IVanillaDependencyStatus
    {
        bool TryGetBounds(out MapBoundsSnapshot snapshot, out uint version);
    }
}
