using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Process-lifetime façade over <see cref="CityBudgetHost"/>. The per-World
    /// PlayerMoney EntityQuery and tracking dictionaries live in the host.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public sealed class CityBudgetFacade
    {
        internal CityBudgetHost? CurrentHost;
    }
}
