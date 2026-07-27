using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Interfaces.Services
{
    [InfrastructureService]
    public interface IPlanetaryClockReader : IVanillaDependencyStatus
    {
        bool TryGetClock(out int dayOfYear, out float currentHour);
    }
}
