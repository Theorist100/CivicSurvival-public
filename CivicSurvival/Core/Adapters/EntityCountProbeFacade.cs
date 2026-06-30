using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Process-lifetime façade over <see cref="EntityCountProbeHost"/>. The 10×
    /// per-World EntityQueries live in the host.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public sealed class EntityCountProbeFacade
    {
        internal EntityCountProbeHost? CurrentHost;
    }
}
