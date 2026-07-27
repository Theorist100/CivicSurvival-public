using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helpers live with the City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182

namespace CivicSurvival.Domains.GridWarfare.Placement
{
    /// <summary>
    /// Shared transaction shape for GridWarfare's outbound launcher emplacements (drone and
    /// ballistic). Both are field emplacements paid off the books: multi-instance (no single-HQ
    /// guard), shadow-wallet only, no credit path, no crew, no road snap — they differ solely in
    /// which prefab marker identifies them, which cost they read, which installation component
    /// they create, and which player-facing reasons they surface.
    ///
    /// Kept as one base instead of two parallel handlers on purpose: the placement pipeline
    /// (prepare / resolve / budget / commit / refund) is identical, so a copy would drift the
    /// moment that pipeline changes. Subclasses stay declarative — markers, cost, installation,
    /// reasons, log labels.
    ///
    /// Stateless: resolves the current World's shadow wallet per call, so a single registered
    /// instance is safe across world reloads.
    /// </summary>
    public abstract class ShadowFieldLauncherPlacementHandler : IBuildingPlacementHandler
    {
        // ── subclass surface ────────────────────────────────────────────────────────────────
        public abstract BuildingPlacementKind Kind { get; }
        public abstract RequestKind RequestKind { get; }
        public abstract string ResultBridgeKey { get; }
        public abstract ReasonId CancelReasonId { get; }

        /// <summary>Reason for a rejected placement (bad prefab / generic failure).</summary>
        protected abstract ReasonId PlacementFailedReasonId { get; }

        /// <summary>Reason for a placement the shadow wallet cannot cover.</summary>
        protected abstract ReasonId InsufficientShadowFundsReasonId { get; }

        /// <summary>Reason for a placed object that lost its Transform before commit.</summary>
        protected abstract ReasonId BuildingLostReasonId { get; }

        /// <summary>Human-readable launcher name for logs and failure messages ("Drone launcher").</summary>
        protected abstract string LauncherLabel { get; }

        /// <summary>Log channel of the owning subclass.</summary>
        protected abstract LogContext Log { get; }

        /// <summary><c>BudgetSource</c> constant used when refunding a rejected placement.</summary>
        protected abstract string BudgetRefundSource { get; }

        /// <summary>Operation label for the shadow deduction ("DroneLauncherInstall").</summary>
        protected abstract string DeductOperationLabel { get; }

        /// <summary>Operation label for the synchronous shadow refund.</summary>
        protected abstract string RefundOperationLabel { get; }

        /// <summary>Prefix of the stable per-placement refund key.</summary>
        protected abstract string RefundOperationKeyPrefix { get; }

        /// <summary>True when the prefab carries this launcher's derived marker component.</summary>
        protected abstract bool HasPrefabMarker(EntityManager em, Entity prefabEntity);

        /// <summary>Marker component name, for the "marker missing" diagnostic.</summary>
        protected abstract string PrefabMarkerName { get; }

        /// <summary>Shadow Cash cost of one emplacement, read from the live balance config.</summary>
        protected abstract int ResolveCost();

        /// <summary>Create this launcher's persisted installation entity on commit.</summary>
        protected abstract void AddInstallation(EntityCommandBuffer ecb, Entity launcherEntity, in BuildingPlacementIntent intent, int paidShadow);

        // ── shared pipeline ─────────────────────────────────────────────────────────────────
        public bool TryPrepareActivation(World world, PrefabBase prefab, Entity prefabEntity, byte mode, out ReasonId reasonId, out string message)
        {
            var em = world.EntityManager;
            if (!HasPrefabMarker(em, prefabEntity))
            {
                reasonId = PlacementFailedReasonId;
                message = $"{PrefabMarkerName} marker missing: {prefab.name}";
                return false;
            }

            int cost = ResolveCost();
            if (cost > 0 && !CanAffordShadow(cost))
            {
                reasonId = InsufficientShadowFundsReasonId;
#pragma warning disable CA1308 // False positive: the rule guards case normalization used for
                // COMPARISON (the Turkish-I class of bugs). This is display text shown to the player,
                // and the label ("Drone launcher") sits mid-sentence here — upper-casing it would print
                // "INSUFFICIENT SHADOW FUNDS FOR DRONE LAUNCHER". Nothing compares this string.
                message = $"Insufficient shadow funds for {LauncherLabel.ToLowerInvariant()} (need ${cost:N0})";
#pragma warning restore CA1308
                return false;
            }

            reasonId = ReasonId.None;
            message = string.Empty;
            return true;
        }

        public bool TryResolvePlacement(
            World world,
            in BuildingPlacementResolveContext ctx,
            EntityCommandBuffer ecb,
            Entity intentEntity,
            out BuildingPlacementResolution resolution,
            out BuildingPlacementAbort abort)
        {
            resolution = default;
            abort = default;

            var em = world.EntityManager;
            Entity prefabEntity = ctx.PrefabEntity;
            Entity matchedEntity = ctx.MatchedBuilding;

            if (!HasPrefabMarker(em, prefabEntity))
            {
                abort = new BuildingPlacementAbort(PlacementFailedReasonId,
                    $"Matched entity {matchedEntity.Index} but prefab {prefabEntity.Index} has no {PrefabMarkerName}", deleteGhost: true);
                return false;
            }

            if (!em.HasComponent<Transform>(matchedEntity))
            {
                abort = new BuildingPlacementAbort(BuildingLostReasonId,
                    $"{LauncherLabel} {matchedEntity.Index} has no Transform — destroying ghost building", deleteGhost: true);
                return false;
            }

            int cost = ResolveCost();
            bool requiresBudget = cost > 0;
            if (requiresBudget && !CanAffordShadow(cost))
            {
                abort = new BuildingPlacementAbort(InsufficientShadowFundsReasonId,
                    $"{LauncherLabel}: insufficient shadow funds (need ${cost:N0}), destroying ghost building", deleteGhost: true);
                return false;
            }

            // Field emplacement — no road snap (unlike the Propaganda Center); the launcher stands
            // wherever the player dropped it, like an AA installation.
            Log.Info($"{LauncherLabel} placement detected: entity={matchedEntity.Index}, cost={cost}");
            resolution = new BuildingPlacementResolution(cost, requiresBudget, (byte)0, PlacementPurse.ShadowWallet);
            return true;
        }

        // No credit path — this is never invoked (ReservedCreditKind stays 0), but the interface
        // requires it. Resolve to a benign success so no code path stalls if it is ever reached.
        public void ResolveCredit(World world, ref BuildingPlacementIntent intent)
        {
            intent.CreditResolved = true;
            intent.CreditSucceeded = true;
        }

        public void ResolveBudget(World world, ref BuildingPlacementIntent intent)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            var result = BudgetTransactionResolver.Deduct(
                world,
                wallet,
                intent.Cost,
                BudgetCategory.ShadowOps,
                DeductOperationLabel);

            intent.BudgetResolved = true;
            intent.BudgetSucceeded = result.Succeeded;

            if (result.Succeeded)
                Log.Info($"{LauncherLabel} budget resolved (success) for building {intent.Building.Index}:{intent.Building.Version}");
            else
                Log.Warn($"{LauncherLabel} budget resolved (failed) for building {intent.Building.Index}:{intent.Building.Version}");
        }

        public void Commit(World world, EntityCommandBuffer ecb, Entity intentEntity, in BuildingPlacementIntent intent)
        {
            int paidShadow = intent.RequiresBudget ? intent.Cost : 0;

            var launcherEntity = ecb.CreateEntity();
            AddInstallation(ecb, launcherEntity, intent, paidShadow);
            ecb.AddComponent<Simulate>(launcherEntity);

            Log.Info($"{LauncherLabel} confirmed: building={intent.Building.Index}, cost={intent.Cost} — outbound strikes launch from here");
        }

        // Shadow-only placement: no credit to return.
        public bool RefundCredit(World world, byte reservedCreditKind, int placementId) => false;

        public string BudgetRefundOperationKey(int placementId) => $"{RefundOperationKeyPrefix}:{placementId}";

        // Never reached for these handlers — the shadow purse always settles through
        // TryRefundBudgetSync below. Implemented for interface completeness (and correctness
        // if a future variant ever pays from the city budget).
        public void QueueBudgetRefund(EntityCommandBuffer ecb, int cost, string operationKey)
        {
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                cost,
                BudgetRefundSource,
                BudgetIncomeKind.Refund,
                operationKey,
                out _,
                BudgetResultMode.RetainResult);
        }

        public bool TryRefundBudgetSync(World world, in BuildingPlacementIntent intent, out bool succeeded)
        {
            succeeded = false;
            if ((PlacementPurse)intent.PayingPurse != PlacementPurse.ShadowWallet)
                return false;

            // Shadow money must return to the shadow wallet, never to the city treasury.
            // TryApplyRefund is idempotent on the operation key and bypasses freeze/act gates
            // (it returns money already deducted).
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            succeeded = wallet.TryApplyRefund(
                intent.Cost,
                RefundOperationLabel,
                BudgetRefundOperationKey(intent.PlacementId));
            return true;
        }

        public ReasonId MapTerminalReasonToReasonId(BuildingPlacementTerminalReason reason)
        {
            if (reason == BuildingPlacementTerminalReason.BudgetFailed)
                return InsufficientShadowFundsReasonId;
            if (reason == BuildingPlacementTerminalReason.BuildingMissingBeforeApply)
                return BuildingLostReasonId;
            return PlacementFailedReasonId;
        }

        protected static bool CanAffordShadow(int cost)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            return wallet.CanAffordWithPending(cost).Affordable;
        }
    }
}
