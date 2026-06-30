using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Interfaces.Services
{
    [InfrastructureService]
    public interface IAreaCollectReader : IVanillaDependencyStatus
    {
        bool DistrictsUpdated { get; }
    }
}
