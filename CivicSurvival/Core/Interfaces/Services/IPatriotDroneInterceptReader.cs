using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Read-only interface for the global "Patriot intercepts drones" player toggle.
    /// Allows cross-domain systems (the AirDefense targeting orchestrator) to read the
    /// flag without importing AirDefense.Systems directly (АКСІОМА 5).
    /// Registered by AirDefenseStateSystem (the single owner of the persisted flag)
    /// via ServiceRegistry.
    /// Null-object: false is fail-closed — when AirDefense is unavailable, Patriot does
    /// NOT engage drones (its ballistic role, BallisticDefenseSystem, is unaffected).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IPatriotDroneInterceptReader
    {
        [NullReturn(false)]
        bool PatriotInterceptsDrones { get; }
    }
}
