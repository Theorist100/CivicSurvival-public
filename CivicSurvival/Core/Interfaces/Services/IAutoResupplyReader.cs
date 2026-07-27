using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Read-only interface for the per-save "AA auto-resupply" player rule.
    /// Lets the AA ammo system read the flag without importing AirDefense.Systems
    /// directly (АКСІОМА 5). Registered by AirDefenseStateSystem (the single owner of
    /// the persisted flag) via ServiceRegistry.
    /// Null-object: true is fail-OPEN — when AirDefense is unavailable the calm-phase
    /// trickle refill still runs (the default behavior), so AA is never silently starved
    /// of its automatic restock by a missing owner.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAutoResupplyReader
    {
        [NullReturn(true)]
        bool AutoResupplyEnabled { get; }
    }
}
