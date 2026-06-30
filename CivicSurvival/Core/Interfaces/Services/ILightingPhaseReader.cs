using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    [InfrastructureService]
    public interface ILightingPhaseReader : IVanillaDependencyStatus
    {
        LightingPhase CurrentPhase { get; }
        bool IsDawnOrDuskLaunchWindow { get; }
    }
}
