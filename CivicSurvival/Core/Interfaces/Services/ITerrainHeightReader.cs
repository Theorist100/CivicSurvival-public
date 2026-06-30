using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;
using Unity.Mathematics;

namespace CivicSurvival.Core.Interfaces.Services
{
    [InfrastructureService]
    public interface ITerrainHeightReader : IVanillaDependencyStatus
    {
        bool TrySampleHeight(float3 worldPosition, out float height);
    }
}
