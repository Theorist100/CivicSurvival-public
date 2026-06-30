using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Pause-safe command surface for the "Patriot intercepts drones" player toggle.
    /// The implementation applies the new value synchronously on the calling (UI) thread
    /// before returning, so the setting takes effect while the simulation is paused. The
    /// toggle must not depend on a later GameSimulation update to apply: defence settings
    /// are routinely changed from the paused build view, and a pause-deferred apply leaves
    /// the UI button stuck "Processing…" until the next unpause.
    /// Implemented and registered by AirDefenseStateSystem, the single owner/writer of the
    /// persisted flag (mirrors how AA placement is hosted by its owning command system).
    /// </summary>
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IPatriotDroneToggleCommandService
    {
        /// <summary>
        /// Sets the persisted "Patriot intercepts drones" flag to <paramref name="enabled"/>
        /// synchronously. Idempotent SET (not a flip): callers pass the target state.
        /// </summary>
        void SetPatriotDroneInterceptImmediate(bool enabled);
    }
}
