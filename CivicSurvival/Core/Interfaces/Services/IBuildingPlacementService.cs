using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Pause-safe generic building-placement command surface used by UI triggers.
    /// Any mod building the player places (AA installations, Propaganda Center, gas
    /// station) goes through this one service, parameterized by
    /// <see cref="BuildingPlacementKind"/>.
    ///
    /// Implementations MUST activate the vanilla placement tool and publish the placement
    /// pending state synchronously before returning. Build placement is commonly started
    /// while the simulation is paused, so callers must not depend on a later ECS update to
    /// make the placement request visible (Axiom 14).
    ///
    /// Cross-cutting Core infrastructure — not owned by any one domain (AA, Propaganda
    /// Center and gas station all reach it through Core), so it is classified
    /// <see cref="InfrastructureServiceAttribute"/> rather than
    /// <c>[OwnedByFeatureId]</c> (CIVIC463).
    /// </summary>
    [InfrastructureService]
    public interface IBuildingPlacementService
    {
        /// <summary>
        /// Attempts to activate the vanilla placement tool for the given kind's prefab.
        /// Success means both the tool activation and the corresponding
        /// <c>BuildingPlacementPending</c> publication completed in this call.
        /// </summary>
        BuildingPlacementActivationResult TryActivatePlacement(
            BuildingPlacementKind kind,
            string prefabName,
            byte mode,
            RequestToken token);

        /// <summary>
        /// Returns the vanilla tool to Default when one of OUR placements is active
        /// (no-op otherwise, so a stale UI click can never close an unrelated vanilla
        /// tool). Completion of the pending request is owned by the lifecycle system,
        /// which already treats "tool back at Default with no placed entity" as the
        /// cancel signal — this is the same path Esc takes.
        /// </summary>
        void CancelActivePlacement();
    }

    public readonly struct BuildingPlacementActivationResult
    {
        public readonly bool Activated;
        public readonly ReasonId ReasonId;
        public readonly string Message;

        private BuildingPlacementActivationResult(bool activated, ReasonId reasonId, string message)
        {
            Activated = activated;
            ReasonId = reasonId;
            Message = message ?? string.Empty;
        }

        public static BuildingPlacementActivationResult Success() =>
            new(true, ReasonId.None, string.Empty);

        public static BuildingPlacementActivationResult Failure(ReasonId reasonId, string message) =>
            new(false, reasonId, message);
    }
}
