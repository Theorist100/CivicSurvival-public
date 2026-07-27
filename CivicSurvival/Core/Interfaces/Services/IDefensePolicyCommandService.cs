using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Pause-safe command surface for the defense-policy selector. The implementation
    /// applies the new policy synchronously on the calling (UI) thread before returning,
    /// so the radio takes effect while the simulation is paused. Defense posture is
    /// routinely changed from the paused build view; the previous ECB → request-entity →
    /// GameSimulation consumer never ran in pause (mirrors IPatriotDroneToggleCommandService).
    /// Implemented and registered by AirDefensePolicySystem, the single owner/writer of the
    /// persisted policy.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IDefensePolicyCommandService
    {
        /// <summary>
        /// Sets the persisted defense policy synchronously. Idempotent SET:
        /// callers pass the target policy.
        /// </summary>
        void SetDefensePolicyImmediate(DefensePolicy policy);
    }
}
