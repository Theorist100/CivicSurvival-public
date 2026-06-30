using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Process-lifetime façade over <see cref="PopulationCountHost"/>.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public sealed class PopulationCountFacade
    {
        internal PopulationCountHost? CurrentHost;
    }
}
