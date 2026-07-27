using Unity.Entities;
using Game.Prefabs;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Context handed to a placement handler at detection time — the matched placed
    /// object plus the player's chosen prefab and mode.
    /// </summary>
    public readonly struct BuildingPlacementResolveContext
    {
        /// <summary>The prefab entity the player was placing.</summary>
        public readonly Entity PrefabEntity;

        /// <summary>The freshly Created placed object entity that matched the prefab.</summary>
        public readonly Entity MatchedBuilding;

        /// <summary>Handler-interpreted placement mode carried from the pending signal.</summary>
        public readonly byte Mode;

        /// <summary>Bridge request id carried from the UI placement command (0 if none).</summary>
        public readonly int RequestId;

        public BuildingPlacementResolveContext(Entity prefabEntity, Entity matchedBuilding, byte mode, int requestId)
        {
            PrefabEntity = prefabEntity;
            MatchedBuilding = matchedBuilding;
            Mode = mode;
            RequestId = requestId;
        }
    }

    /// <summary>
    /// The transaction shape a handler resolved for a placement — what the generic
    /// pipeline stamps onto <c>BuildingPlacementIntent</c>. The handler additionally
    /// adds any building-specific payload component to the intent entity itself.
    /// </summary>
    public readonly struct BuildingPlacementResolution
    {
        public readonly int Cost;
        public readonly bool RequiresBudget;
        public readonly byte ReservedCreditKind;
        public readonly PlacementPurse PayingPurse;

        public BuildingPlacementResolution(int cost, bool requiresBudget, byte reservedCreditKind, PlacementPurse payingPurse)
        {
            Cost = cost;
            RequiresBudget = requiresBudget;
            ReservedCreditKind = reservedCreditKind;
            PayingPurse = payingPurse;
        }
    }

    /// <summary>
    /// A handler's decision to abort a placement at detection time (bad prefab, no
    /// transform, credit/funds unavailable). The generic detector emits the failure,
    /// optionally deletes the ghost object, and clears the pending state.
    /// </summary>
    public readonly struct BuildingPlacementAbort
    {
        public readonly ReasonId ReasonId;
        public readonly string Reason;
        public readonly bool DeleteGhost;

        public BuildingPlacementAbort(ReasonId reasonId, string reason, bool deleteGhost)
        {
            ReasonId = reasonId;
            Reason = reason ?? string.Empty;
            DeleteGhost = deleteGhost;
        }
    }

    /// <summary>
    /// Building-specific strategy for the generic Core placement pipeline. One handler
    /// per <see cref="BuildingPlacementKind"/>, registered in
    /// <c>BuildingPlacementHandlerRegistry</c> by the owning domain. The generic
    /// systems (command / detector / payment / commit / lifecycle) own the pause-safe
    /// transaction flow; the handler supplies only the parts that differ per building:
    /// affordability pre-checks, credit/budget resolution, and what mod entity to create.
    ///
    /// Handlers are stateless strategy objects — every method receives the current
    /// <see cref="World"/> and resolves its collaborators per call, so a single instance
    /// is safe across world reloads.
    /// </summary>
    public interface IBuildingPlacementHandler
    {
        /// <summary>Kind discriminator this handler serves.</summary>
        BuildingPlacementKind Kind { get; }

        /// <summary>Request kind used for the placement result lifecycle.</summary>
        RequestKind RequestKind { get; }

        /// <summary>RequestResultBridge key for this placement's request lifecycle.</summary>
        string ResultBridgeKey { get; }

        /// <summary>Reason id surfaced when the player cancels the placement (tool returns to Default).</summary>
        ReasonId CancelReasonId { get; }

        /// <summary>
        /// Synchronous pre-activation check on the UI thread: the prefab is a valid target
        /// for this kind and the player can afford the non-budget prerequisites (e.g. crew).
        /// Returns false with a reason to reject before the tool is activated.
        /// </summary>
        bool TryPrepareActivation(World world, PrefabBase prefab, Entity prefabEntity, byte mode, out ReasonId reasonId, out string message);

        /// <summary>
        /// Resolve the placed object into a transaction: decide cost / budget / reserved credit,
        /// stamp any building-specific payload component onto <paramref name="intentEntity"/> via
        /// <paramref name="ecb"/>, and return the generic <see cref="BuildingPlacementResolution"/>.
        /// Return false with an <see cref="BuildingPlacementAbort"/> to reject the placement.
        /// </summary>
        bool TryResolvePlacement(
            World world,
            in BuildingPlacementResolveContext ctx,
            EntityCommandBuffer ecb,
            Entity intentEntity,
            out BuildingPlacementResolution resolution,
            out BuildingPlacementAbort abort);

        /// <summary>Resolve a reserved credit against the owning singleton and write the outcome onto the intent.</summary>
        void ResolveCredit(World world, ref BuildingPlacementIntent intent);

        /// <summary>Resolve the paid budget deduction and write the outcome onto the intent.</summary>
        void ResolveBudget(World world, ref BuildingPlacementIntent intent);

        /// <summary>Create the mod entity/components for a successfully committed placement.</summary>
        void Commit(World world, EntityCommandBuffer ecb, Entity intentEntity, in BuildingPlacementIntent intent);

        /// <summary>Return a reserved credit on reject. Returns true if the credit was returned.</summary>
        bool RefundCredit(World world, byte reservedCreditKind, int placementId);

        /// <summary>Stable per-placement budget-refund operation key (matched across save/load).</summary>
        string BudgetRefundOperationKey(int placementId);

        /// <summary>Queue a budget refund for a rejected paid placement (handler owns the budget source).</summary>
        void QueueBudgetRefund(EntityCommandBuffer ecb, int cost, string operationKey);

        /// <summary>
        /// Settle the budget refund synchronously when the paying purse has no deferred
        /// request pipeline (e.g. a shadow-wallet purchase). Returns true when the handler
        /// took ownership of the refund — <paramref name="succeeded"/> then carries the
        /// outcome and the generic queue/drain path must NOT run (it would refund the
        /// wrong purse). Returns false to let the queued city-budget refund proceed.
        /// </summary>
        bool TryRefundBudgetSync(World world, in BuildingPlacementIntent intent, out bool succeeded);

        /// <summary>Map a terminal reason onto the handler's player-facing reason id.</summary>
        ReasonId MapTerminalReasonToReasonId(BuildingPlacementTerminalReason reason);
    }
}
