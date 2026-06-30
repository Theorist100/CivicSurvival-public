using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Pause-safe AA placement command surface used by UI triggers.
    /// Implementations must activate the vanilla placement tool and publish the
    /// placement pending state synchronously before returning. Build placement is
    /// commonly started while the simulation is paused, so callers must not depend
    /// on a later ECS update to make the placement request visible.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAAPlacementCommandService
    {
        /// <summary>
        /// Attempts to activate the vanilla placement tool for an AA prefab.
        /// Success means both the tool activation and the corresponding
        /// <see cref="AAPlacementPending"/> publication completed in this call.
        /// </summary>
        AAPlacementActivationResult TryActivatePlacement(
            string prefabName,
            AAPlacementMode mode,
            RequestToken token);
    }

    public readonly struct AAPlacementActivationResult
    {
        public readonly bool Activated;
        public readonly ReasonId ReasonId;
        public readonly string Message;

        private AAPlacementActivationResult(bool activated, ReasonId reasonId, string message)
        {
            Activated = activated;
            ReasonId = reasonId;
            Message = message ?? string.Empty;
        }

        public static AAPlacementActivationResult Success() =>
            new(true, ReasonId.None, string.Empty);

        public static AAPlacementActivationResult Failure(ReasonId reasonId, string message) =>
            new(false, reasonId, message);
    }
}
