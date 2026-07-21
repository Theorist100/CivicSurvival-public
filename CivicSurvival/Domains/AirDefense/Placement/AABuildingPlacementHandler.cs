using System;
using Game;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Domain.Mobilization;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helpers live with the City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
using CivicSurvival.Domains.AirDefense.Systems;

namespace CivicSurvival.Domains.AirDefense.Placement
{
    /// <summary>
    /// Air-defense consumer of the generic Core building-placement pipeline. Encapsulates every
    /// AA-specific step (prefab marker + crew pre-check, transform/credit/affordability resolution,
    /// city-scaled magazine, AirDefenseInstallation commit, credit + budget refunds) that used to
    /// live in the four AA placement systems, behind the shared <see cref="IBuildingPlacementHandler"/>.
    ///
    /// Stateless: resolves the current World's AA state owner + services per call, so a single
    /// registered instance is safe across world reloads.
    /// </summary>
    [HandlesRequestKind(RequestKind.AirDefensePlacement)]
    public sealed class AABuildingPlacementHandler : IBuildingPlacementHandler
    {
        private static readonly LogContext Log = new("AABuildingPlacement");

        public BuildingPlacementKind Kind => BuildingPlacementKind.AirDefense;
        public RequestKind RequestKind => RequestKind.AirDefensePlacement;
        public string ResultBridgeKey => RequestResultBridge.AirDefensePlacement;
        public ReasonId CancelReasonId => ReasonIds.AaCancelled;

        public bool TryPrepareActivation(World world, PrefabBase prefab, Entity prefabEntity, byte mode, out ReasonId reasonId, out string message)
        {
            var em = world.EntityManager;
            if (!em.HasComponent<AirDefensePrefabData>(prefabEntity))
            {
                reasonId = ReasonIds.AaPlacementFailed;
                message = $"AirDefensePrefabData marker missing: {prefab.name}";
                return false;
            }

            var prefabData = em.GetComponentData<AirDefensePrefabData>(prefabEntity);
            var aaMode = (AAPlacementMode)mode;
            int crewRequired = aaMode == AAPlacementMode.Heritage
                ? AAParams.ForType(BalanceConfig.Current, AAType.HeritageBofors).CrewRequired
                : prefabData.CrewRequired;

            var manpower = ServiceRegistry.Instance.Require<IMobilizationManpowerReader>();
            if (!manpower.CanRecruit(crewRequired))
            {
                reasonId = ReasonIds.AaInsufficientManpower;
                message = $"Insufficient manpower: {crewRequired} required, {manpower.AvailableManpower} available";
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

            if (!em.HasComponent<AirDefensePrefabData>(prefabEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.AaPlacementFailed,
                    $"Matched entity {matchedEntity.Index} but prefab {prefabEntity.Index} has no AirDefensePrefabData", deleteGhost: true);
                return false;
            }

            var prefabData = em.GetComponentData<AirDefensePrefabData>(prefabEntity);
            Log.Info($"AA placement detected: entity={matchedEntity.Index}, type={prefabData.Type}");

            // Transform gate (the placed object must be a real, positioned object).
            if (!em.HasComponent<Transform>(matchedEntity))
            {
                abort = new BuildingPlacementAbort(ReasonIds.AaBuildingLost,
                    $"AA building {matchedEntity.Index} has no Transform — destroying ghost building", deleteGhost: true);
                return false;
            }

            var aaMode = (AAPlacementMode)ctx.Mode;

            bool usedHeritage = false;
            var state = world.GetExistingSystemManaged<AirDefenseStateSystem>();
            if (aaMode == AAPlacementMode.Heritage)
            {
                usedHeritage = state != null && state.IsHeritageCreditAvailable();
                if (!usedHeritage)
                {
                    abort = new BuildingPlacementAbort(ReasonIds.AaPlacementFailed,
                        "AA installation aborted: heritage prefab requires an available heritage credit", deleteGhost: true);
                    return false;
                }
            }

            bool usedDonorCredit = false;
            if (aaMode == AAPlacementMode.DonorCredit)
            {
                if (prefabData.Type != AAType.PatriotSAM && prefabData.Type != AAType.HawkSAM)
                {
                    abort = new BuildingPlacementAbort(ReasonIds.AaPlacementFailed,
                        "AA installation aborted: donor credit intent requires a donated SAM (Patriot or Hawk) prefab", deleteGhost: true);
                    return false;
                }

                usedDonorCredit = state != null && state.IsDonorPatriotCreditAvailable();
                if (!usedDonorCredit)
                {
                    abort = new BuildingPlacementAbort(ReasonIds.AaPlacementFailed,
                        "AA installation aborted: donor Patriot credit is no longer available", deleteGhost: true);
                    return false;
                }
            }

            var cfg = BalanceConfig.Current;
            var heritageP = AAParams.ForType(cfg, AAType.HeritageBofors);

            // Stats as fielded through THIS source: the market sells the same barrel tuned past its
            // official spec, so the option — not the prefab — decides what gets stamped and charged.
            bool boughtOnMarket = aaMode == AAPlacementMode.BlackMarket;
            var sourceP = AAParams.ForOption(cfg, prefabData.Type, aaMode);

            bool requiresBudget = !usedHeritage && !usedDonorCredit && sourceP.Price > 0;
            if (requiresBudget)
            {
                if (boughtOnMarket)
                {
                    var marketWallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                    if (!AirDefenseEligibility.CanPlaceShadowAA(
                            sourceP.Price, int.MaxValue, sourceP.CrewRequired, marketWallet, out var marketReason))
                    {
                        abort = new BuildingPlacementAbort(ReasonIds.AaBudgetFailed,
                            $"AA installation: shadow purchase refused ({marketReason}, need {sourceP.Price:N0}), destroying ghost building",
                            deleteGhost: true);
                        return false;
                    }
                }
                else if (!AirDefenseEligibility.CanPayAirDefenseBudget(sourceP.Price, world, out _))
                {
                    abort = new BuildingPlacementAbort(ReasonIds.AaBudgetFailed,
                        $"AA installation: insufficient funds (need ${sourceP.Price:N0}), destroying ghost building", deleteGhost: true);
                    return false;
                }
            }

            // City-scaled magazine: stamp once at placement off the city SIZE (built nameplate via the
            // snapshot, NOT live production — same "city size" the wave count uses).
            int cityMW = WaveContextGatherer.MIN_PRODUCTION_MW;
            var powerGridQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            if (powerGridQuery.TryGetSingleton<PowerGridSingleton>(out var powerGrid))
                cityMW = WaveContextGatherer.ResolveCitySizeMW(powerGrid.Production);
            powerGridQuery.Dispose();

            int scaledMaxAmmo = AAAmmoScaling.ScaleMaxAmmo(
                cfg, usedHeritage ? heritageP.MaxAmmo : sourceP.MaxAmmo, cityMW);

            // Credit and purse are orthogonal: a credit consumes a counter (no money), a paid
            // placement moves money out of the purse below. Both are persisted on the intent so
            // the choice survives a save taken between detection and payment.
            var creditKind = AAPlacementCreditKind.None;
            if (usedHeritage) creditKind = AAPlacementCreditKind.Heritage;
            else if (usedDonorCredit) creditKind = AAPlacementCreditKind.DonorPatriot;
            var purse = boughtOnMarket ? PlacementPurse.ShadowWallet : PlacementPurse.CityBudget;

            ecb.AddComponent(intentEntity, new AAInstallationPayload
            {
                ResolvedType = usedHeritage ? AAType.HeritageBofors : prefabData.Type,
                Range = usedHeritage ? heritageP.Range : sourceP.Range,
                InterceptChanceShahed = usedHeritage ? heritageP.InterceptChanceShahed : sourceP.InterceptChanceShahed,
                InterceptChanceBallistic = usedHeritage ? heritageP.InterceptChanceBallistic : sourceP.InterceptChanceBallistic,
                MaxAmmo = scaledMaxAmmo,
                CooldownDuration = usedHeritage ? heritageP.CooldownDuration : sourceP.CooldownDuration,
                CrewRequired = usedHeritage ? heritageP.CrewRequired : sourceP.CrewRequired
            });

            resolution = new BuildingPlacementResolution(
                requiresBudget ? sourceP.Price : 0,
                requiresBudget,
                (byte)creditKind,
                purse);
            return true;
        }

        public void ResolveCredit(World world, ref BuildingPlacementIntent intent)
        {
            var state = world.GetExistingSystemManaged<AirDefenseStateSystem>();
            if (state == null)
            {
                intent.CreditResolved = true;
                intent.CreditSucceeded = false;
                Log.Warn($"Credit claim could not be resolved: AirDefenseStateSystem missing (plId={intent.PlacementId})");
                return;
            }

            state.ResolvePlacementCredit((AAPlacementCreditKind)intent.ReservedCreditKind, intent.PlacementId, out var creditSucceeded);
            intent.CreditResolved = true;
            intent.CreditSucceeded = creditSucceeded;
        }

        public void ResolveBudget(World world, ref BuildingPlacementIntent intent)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            // The purse is whatever detection reserved. ShadowOps routes the deduction into the
            // wallet (BudgetTransactionResolver) and picks up the sanctions markup on the way.
            bool shadowFunded = (PlacementPurse)intent.PayingPurse == PlacementPurse.ShadowWallet;
            var result = BudgetTransactionResolver.Deduct(
                world,
                wallet,
                intent.Cost,
                shadowFunded ? BudgetCategory.ShadowOps : BudgetCategory.AirDefense,
                shadowFunded ? "AAInstallShadow" : "AAInstall");

            intent.BudgetResolved = true;
            intent.BudgetSucceeded = result.Succeeded;

            if (result.Succeeded)
                Log.Info($"Budget resolved (success) for building {intent.Building.Index}:{intent.Building.Version}");
            else
                Log.Warn($"Budget resolved (failed) for building {intent.Building.Index}:{intent.Building.Version}");
        }

        public void Commit(World world, EntityCommandBuffer ecb, Entity intentEntity, in BuildingPlacementIntent intent)
        {
            var em = world.EntityManager;
            if (!em.HasComponent<AAInstallationPayload>(intentEntity))
            {
                Log.Error($"AAInstallationPayload missing at commit (building={intent.Building.Index}) — installation not created");
                return;
            }

            var payload = em.GetComponentData<AAInstallationPayload>(intentEntity);

            // New installations start partially loaded so a freshly built gun cannot bypass the
            // graduated calm-phase refill. Missile launchers (Patriot, Hawk) are exempt: their
            // rockets are excluded from the trickle, so a partial start could never top up — they
            // start full (price/gate close the abuse loop instead).
            var balance = BalanceConfig.Current;
            float startFraction = payload.ResolvedType.FiresInterceptorMissile()
                ? balance.AAUnits.PatriotStartAmmoFraction
                : balance.AirDefense.StartAmmoFraction;
            int startAmmo = Math.Clamp(
                (int)Math.Round(payload.MaxAmmo * startFraction),
                0,
                payload.MaxAmmo);

            // Only a budget-paid placement refunds cash on demolition; credit/heritage stamp PaidBudget=0.
            int paidBudget = intent.RequiresBudget ? intent.Cost : 0;
            if (!GameTimeSystem.TryGetGameHours(out float placedGameHours))
                placedGameHours = 0f;

            var aaEntity = ecb.CreateEntity();
            ecb.AddComponent(aaEntity, new AirDefenseInstallation
            {
                Building = intent.Building,
                Type = payload.ResolvedType,
                Range = payload.Range,
                InterceptChanceShahed = payload.InterceptChanceShahed,
                InterceptChanceBallistic = payload.InterceptChanceBallistic,
                CurrentAmmo = startAmmo,
                MaxAmmo = payload.MaxAmmo,
                CooldownDuration = payload.CooldownDuration,
                CrewAssigned = 0,
                CrewRequired = payload.CrewRequired,
                PaidBudget = paidBudget,
                PlacedGameHours = placedGameHours
            });

            ecb.AddComponent(aaEntity, new AirDefenseCooldown { ReadyAtGameSeconds = 0 });
            ecb.AddComponent(aaEntity, new RequestCrewTag { CrewRequired = payload.CrewRequired });
            ecb.AddComponent<Simulate>(aaEntity);

            var state = world.GetExistingSystemManaged<AirDefenseStateSystem>();
            state?.RecordUiStatsInstallationAdded(payload.ResolvedType, startAmmo, payload.MaxAmmo);

            Log.Info($"{payload.ResolvedType} confirmed: building={intent.Building.Index}, crew={payload.CrewRequired}, cost={intent.Cost}");
        }

        public bool RefundCredit(World world, byte reservedCreditKind, int placementId)
        {
            var state = world.GetExistingSystemManaged<AirDefenseStateSystem>();
            if (state == null)
                return false;

            return state.RefundPlacementCredit((AAPlacementCreditKind)reservedCreditKind, placementId);
        }

        public string BudgetRefundOperationKey(int placementId) => $"AAPlacementRefund:{placementId}";

        public void QueueBudgetRefund(EntityCommandBuffer ecb, int cost, string operationKey)
        {
            // A non-positive cost simply means there is no refund to queue; the bool is discarded.
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                cost,
                BudgetSource.AAInstallRefund,
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

            // Shadow money must return to the shadow wallet, never to the city treasury —
            // the queued BudgetAddFunds path would launder it into the budget. TryApplyRefund
            // is idempotent on the operation key and bypasses freeze/act gates (it returns
            // money already deducted).
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            succeeded = wallet.TryApplyRefund(
                intent.Cost,
                "AAInstallShadowRefund",
                BudgetRefundOperationKey(intent.PlacementId));
            return true;
        }

        public ReasonId MapTerminalReasonToReasonId(BuildingPlacementTerminalReason reason)
        {
            if (reason == BuildingPlacementTerminalReason.BudgetFailed)
                return ReasonIds.AaBudgetFailed;
            if (reason == BuildingPlacementTerminalReason.BuildingMissingBeforeApply)
                return ReasonIds.AaBuildingLost;
            if (reason == BuildingPlacementTerminalReason.DuplicateBuilding)
                return ReasonIds.AaDuplicate;
            return ReasonIds.AaPlacementFailed;
        }
    }
}
