using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Pause-safe command surface for the "AA auto-resupply" player rule.
    /// The implementation applies the new value synchronously on the calling (UI) thread
    /// before returning, so the rule takes effect while the simulation is paused. The
    /// toggle must not depend on a later GameSimulation update to apply: defence rules are
    /// routinely changed from the paused build view, and a pause-deferred apply leaves the
    /// UI button stuck "Processing…" until the next unpause.
    /// Implemented and registered by AirDefenseStateSystem, the single owner/writer of the
    /// persisted flag (mirrors IPatriotDroneToggleCommandService).
    /// </summary>
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAutoResupplyToggleCommandService
    {
        /// <summary>
        /// Sets the persisted "AA auto-resupply" flag to <paramref name="enabled"/>
        /// synchronously. Idempotent SET (not a flip): callers pass the target state.
        /// </summary>
        void SetAutoResupplyImmediate(bool enabled);
    }
}
