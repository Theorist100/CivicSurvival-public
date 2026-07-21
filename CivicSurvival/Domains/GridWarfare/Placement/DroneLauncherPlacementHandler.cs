using Game;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.GridWarfare;
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
    /// GridWarfare consumer of the generic Core building-placement pipeline. Places a drone
    /// launcher — a field emplacement that outbound counter-strike drones launch from instead
    /// of the map centre. Multi-instance (no single-HQ guard — any number can be built) and
    /// shadow-only (paid from the Shadow Wallet, off the books, like a shadow-market AA buy).
    /// No credit path, no crew, no road snap.
    ///
    /// Stateless: resolves the current World's shadow wallet per call, so a single registered
    /// instance is safe across world reloads.
    /// </summary>
    [HandlesRequestKind(RequestKind.DroneLauncherPlacement)]
    public sealed class DroneLauncherPlacementHandler : IBuildingPlacementHandler
    {
        private static readonly LogContext Log = new("DroneLauncherPlacement");

        // Fallback when the balance config is not yet loaded (mirrors the contract default).
        private const int FALLBACK_LAUNCHER_COST = 25000;

        public BuildingPlacementKind Kind => BuildingPlacementKind.DroneLauncher;
        public RequestKind RequestKind => RequestKind.DroneLauncherPlacement;
        public string ResultBridgeKey => RequestResultBridge.DroneLauncherPlacement;
        public ReasonId CancelReasonId => ReasonIds.DroneLauncherCancelled;

        public bool TryPrepareActivation(World world, PrefabBase prefab, Entity prefabEntity, byte mode, out ReasonId reasonId, out string message)
        {
            var em = world.EntityManager;
            if (!em.HasComponent<DroneLauncherPrefabData>(prefabEntity))
            {
                reasonId = ReasonIds.DroneLauncherPlacementFailed;
                message = $"DroneLauncherPrefabData marker missing: {prefab.name}";
                return false;
            }

            int cost = ResolveCost();
            if (cost > 0 && !CanAffordShadow(cost))
            {
                reasonId = ReasonIds.DroneLauncherInsufficientShadowFunds;
                message = $"Insufficient shadow funds for drone launcher (need ${cost:N0})";
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

            if (!em.HasComponent<DroneLauncherPrefabData>(prefabEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.DroneLauncherPlacementFailed,
                    $"Matched entity {matchedEntity.Index} but prefab {prefabEntity.Index} has no DroneLauncherPrefabData", deleteGhost: true);
                return false;
            }

            if (!em.HasComponent<Transform>(matchedEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.DroneLauncherBuildingLost,
                    $"Drone launcher {matchedEntity.Index} has no Transform — destroying ghost building", deleteGhost: true);
                return false;
            }

            int cost = ResolveCost();
            bool requiresBudget = cost > 0;
            if (requiresBudget && !CanAffordShadow(cost))
            {
                abort = new BuildingPlacementAbort(ReasonIds.DroneLauncherInsufficientShadowFunds,
                    $"Drone launcher: insufficient shadow funds (need ${cost:N0}), destroying ghost building", deleteGhost: true);
                return false;
            }

            // Field emplacement — no road snap (unlike the Propaganda Center); the launcher stands
            // wherever the player dropped it, like an AA installation.
            Log.Info($"Drone launcher placement detected: entity={matchedEntity.Index}, cost={cost}");
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
                "DroneLauncherInstall");

            intent.BudgetResolved = true;
            intent.BudgetSucceeded = result.Succeeded;

            if (result.Succeeded)
                Log.Info($"Drone launcher budget resolved (success) for building {intent.Building.Index}:{intent.Building.Version}");
            else
                Log.Warn($"Drone launcher budget resolved (failed) for building {intent.Building.Index}:{intent.Building.Version}");
        }

        public void Commit(World world, EntityCommandBuffer ecb, Entity intentEntity, in BuildingPlacementIntent intent)
        {
            int paidShadow = intent.RequiresBudget ? intent.Cost : 0;

            var launcherEntity = ecb.CreateEntity();
            ecb.AddComponent(launcherEntity, new DroneLauncherInstallation
            {
                Building = intent.Building,
                PaidShadow = paidShadow
            });
            ecb.AddComponent<Simulate>(launcherEntity);

            Log.Info($"Drone launcher confirmed: building={intent.Building.Index}, cost={intent.Cost} — outbound drones launch from here");
        }

        // Shadow-only placement: no credit to return.
        public bool RefundCredit(World world, byte reservedCreditKind, int placementId) => false;

        public string BudgetRefundOperationKey(int placementId) => $"DroneLauncherPlacementRefund:{placementId}";

        // Never reached for this handler — the shadow purse always settles through
        // TryRefundBudgetSync below. Implemented for interface completeness (and correctness
        // if a future variant ever pays from the city budget).
        public void QueueBudgetRefund(EntityCommandBuffer ecb, int cost, string operationKey)
        {
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                cost,
                BudgetSource.DroneLauncherInstallRefund,
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
                "DroneLauncherInstallShadowRefund",
                BudgetRefundOperationKey(intent.PlacementId));
            return true;
        }

        public ReasonId MapTerminalReasonToReasonId(BuildingPlacementTerminalReason reason)
        {
            if (reason == BuildingPlacementTerminalReason.BudgetFailed)
                return ReasonIds.DroneLauncherInsufficientShadowFunds;
            if (reason == BuildingPlacementTerminalReason.BuildingMissingBeforeApply)
                return ReasonIds.DroneLauncherBuildingLost;
            return ReasonIds.DroneLauncherPlacementFailed;
        }

        private static bool CanAffordShadow(int cost)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            return wallet.CanAffordWithPending(cost).Affordable;
        }

        private static int ResolveCost()
        {
            var gw = BalanceConfig.Current?.GridWarfare;
            return gw?.DroneLauncherCost ?? FALLBACK_LAUNCHER_COST;
        }
    }
}
