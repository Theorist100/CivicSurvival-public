using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Processes ContractResponse ephemeral entities.
    /// REFACTORED: Contains full business logic (no delegation to service).
    ///
    /// Flow:
    /// 1. UI creates entity with ContractResponse (Status=Pending)
    /// 2. This system finds PendingProcurement on building, creates ServiceContract
    /// 3. Request entity destroyed after processing
    ///
    /// Uses an empty-query gate so retained budget results can drain without a new UI request.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.MaintenanceContract)]
    [TransientConsumerReconcile(typeof(ContractResponse), ReconcileMode.OwnsDurableOutbox, DurableState = typeof(RequestMeta))]
    public partial class ContractResponseSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ContractResponseSystem");

        private EntityQuery m_RequestQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private IShadowReputationService m_ReputationService = null!;
        private IShadowWalletService m_WalletService = null!;
        private MaintenanceContractSystem? m_MaintenanceContracts;
        private ComponentLookup<PendingProcurement> m_PendingProcurementLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private EntityQuery m_ResolvedContractPaymentQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<ContractResponse>()
            );

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_PendingProcurementLookup = GetComponentLookup<PendingProcurement>(false);
            m_BuildingLookup = GetComponentLookup<Building>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_ResolvedContractPaymentQuery = GetEntityQuery(
                ComponentType.ReadOnly<ContractPaymentIntent>(),
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>());

            Log.Info("Created (full business logic)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Self-wiring: resolve cross-domain services when system actually runs
            m_ReputationService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowReputationService.Instance);
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            m_MaintenanceContracts ??= FeatureRegistry.Instance.Require<MaintenanceContractSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty && m_ResolvedContractPaymentQuery.IsEmpty)
                return;

            m_PendingProcurementLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_DeletedLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            if (!m_ResolvedContractPaymentQuery.IsEmpty)
            {
                ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                hasEcb = true;
                DrainResolvedContractPayments(ecb);
            }

            foreach (var (request, meta, requestEntity) in
                SystemAPI.Query<RefRO<ContractResponse>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                var result = ProcessRequest(request.ValueRO, meta.ValueRO, ecb);
                // Contract: ProcessRequest only sets EmitTerminal=true with a
                // terminal status (Success/Failed). RequestStatus.Pending here
                // would be a producer bug — guard defensively and document so
                // CIVIC428 sees the explicit terminal-only intent.
                if (result.EmitTerminal && result.Status != RequestStatus.Pending)
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.MaintenanceContract, result.Status, result.ReasonId, SystemAPI.Time.ElapsedTime);
                ecb.DestroyEntity(requestEntity);
            }

            if (hasEcb)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private readonly struct ContractRequestResult
        {
            public readonly bool EmitTerminal;
            public readonly RequestStatus Status;
            public readonly ReasonId ReasonId;

            private ContractRequestResult(bool emitTerminal, RequestStatus status, ReasonId reasonId)
            {
                EmitTerminal = emitTerminal;
                Status = status;
                ReasonId = reasonId;
            }

            public static ContractRequestResult Pending() => new(false, RequestStatus.Pending, ReasonId.None);
            public static ContractRequestResult Terminal(RequestStatus status, ReasonId reasonId) => new(true, status, reasonId);
        }

        private ContractRequestResult ProcessRequest(in ContractResponse request, in RequestMeta meta, EntityCommandBuffer ecb)
        {
            int entityIndex = request.BuildingEntityIndex;
            int entityVersion = request.BuildingEntityVersion;

            // Find building entity with matching PendingProcurement
            var buildingEntity = FindBuildingWithPendingOffer(entityIndex, entityVersion);
            if (buildingEntity == Entity.Null)
            {
                Log.Warn($"No pending offer for building index {entityIndex}");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.InternalError);
            }

            // Get the pending offer data
            if (!m_PendingProcurementLookup.HasComponent(buildingEntity)
                || !m_PendingProcurementLookup.IsComponentEnabled(buildingEntity))
            {
                Log.Warn($"Offer disabled for entity {entityIndex}");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.InternalError);
            }

            var offer = m_PendingProcurementLookup[buildingEntity];
            if (offer.Lifecycle != PendingProcurementLifecycle.Active)
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "inactive-lifecycle");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Offer for entity {entityIndex} is not active ({offer.Lifecycle})");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.ContractOfferStale);
            }

            switch (request.ResponseType)
            {
                case ContractResponseType.AcceptOfficial:
                    if (!QueueContractPayment(buildingEntity, offer, isShady: false, request.ExpectedPrice, meta, ecb, out var officialFail))
                    {
                        return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonId.FromRuntime(officialFail.ToString()));
                    }
                    Log.Info($"Official {offer.Type} contract payment queued for entity {entityIndex}, price: ${offer.OfficialPrice}");
                    return ContractRequestResult.Pending();

                case ContractResponseType.AcceptShady:
                    var shadyGate = ActionGate.Resolve(
                        ActionKey.ShadyContractAccept,
                        BuildShadowActionContext(proposedCost: 0));
                    if (!shadyGate.CanRun)
                    {
                        Log.Info($"Shady contract blocked for entity {entityIndex}: {shadyGate.LockedReasonId}");
                        return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonId.FromRuntime(shadyGate.LockedReasonId));
                    }
                    if (!QueueContractPayment(buildingEntity, offer, isShady: true, request.ExpectedPrice, meta, ecb, out var shadyFail))
                    {
                        return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonId.FromRuntime(shadyFail.ToString()));
                    }
                    Log.Info($"SHADY {offer.Type} contract payment queued for entity {entityIndex}, price: ${offer.ShadyPrice}, kickback: ${offer.KickbackOffer}");
                    return ContractRequestResult.Pending();

                case ContractResponseType.Decline:
                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "declined");
                    m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                    m_ReputationService.OnOfferRejected();
                    Log.Info($"Offer declined for entity {entityIndex}");
                    return ContractRequestResult.Terminal(RequestStatus.Success, ReasonId.None);

                default:
                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "invalid-response");
                    m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                    Log.Warn($"Unknown response type: {request.ResponseType}");
                    return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.InternalError);
            }
        }

        /// <summary>
        /// Find building entity with enabled PendingProcurement matching the given index.
        /// </summary>
        private Entity FindBuildingWithPendingOffer(int entityIndex, int entityVersion)
        {
            var entity = new Entity { Index = entityIndex, Version = entityVersion };
            if (!m_BuildingLookup.HasComponent(entity) || m_DeletedLookup.HasComponent(entity))
                return Entity.Null;

            if (!m_PendingProcurementLookup.HasComponent(entity) ||
                !m_PendingProcurementLookup.IsComponentEnabled(entity))
                return Entity.Null;

            return entity;
        }

        private bool QueueContractPayment(Entity buildingEntity, PendingProcurement offer, bool isShady, long expectedPrice, in RequestMeta meta, EntityCommandBuffer ecb, out FixedString64Bytes failReason)
        {
            long price = isShady ? offer.ShadyPrice : offer.OfficialPrice;
            failReason = default;

            if (expectedPrice != price)
            {
                failReason = ReasonIds.ProcurementPriceChanged.ToFixedString();
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "price-mismatch");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: expected ${expectedPrice:N0}, current ${price:N0}");
                return false;
            }

            if (HasLiveContractForBuilding(buildingEntity))
            {
                failReason = ReasonIds.ContractOfferStale.ToFixedString();
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "duplicate-live-contract");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: live contract already exists");
                return false;
            }

            if (!CanQueueContractPayment(buildingEntity, price, out failReason))
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-preflight-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                return false;
            }

            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    price,
                    BudgetCategory.Procurement,
                    BudgetPriority.PlayerAction,
                    $"MaintenanceContract:{buildingEntity.Index}:{buildingEntity.Version}",
                    out var budgetEntity,
                    meta,
                    BudgetResultMode.RetainResult))
            {
                failReason = ReasonIds.ContractInsufficientFunds.ToFixedString();
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "budget-queue-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: insufficient funds (${price:N0})");
                return false;
            }

            ecb.AddComponent(budgetEntity, new ContractPaymentIntent
            {
                Building = BuildingRef.FromEntity(buildingEntity),
                Service = offer.Service,
                Type = offer.Type,
                IsShady = isShady,
                Price = price,
                KickbackAmount = isShady ? offer.KickbackOffer : 0,
                Quality = isShady ? offer.ShadyQuality : offer.OfficialQuality,
                VendorNameHash = isShady ? offer.ShadyVendorHash : offer.OfficialVendorHash,
                RequestId = meta.RequestId,
                RequestCreatedTime = meta.CreatedTime,
                RequestCreatedFrame = meta.CreatedFrame,
                RefundOperationKey = new FixedString128Bytes(ContractRefundOperationKey(meta.RequestId, meta.CreatedFrame, meta.CreatedTime))
            });

            // Freeze the selected offer. The intent snapshot is now the source of truth.
            MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "accepted-payment-queued");
            m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
            return true;
        }

        private ActionContext BuildShadowActionContext(long proposedCost)
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                hasWaveState: false,
                currentPhase: GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar).WithCost(proposedCost);

            return m_WalletService.IsOperational
                ? ctx.WithWalletState(m_WalletService.IsFrozen, m_WalletService.Balance, m_WalletService.SanctionsMarkup)
                : ctx;
        }

        private bool CanQueueContractPayment(Entity buildingEntity, long price, out FixedString64Bytes failReason)
        {
            failReason = default;
            if (price <= 0)
            {
                failReason = ReasonIds.ContractInvalidPrice.ToFixedString();
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: invalid price {price}");
                return false;
            }

            if (!CityBudgetService.CanAffordWithPending(World, price))
            {
                failReason = ReasonIds.ContractInsufficientFunds.ToFixedString();
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: insufficient funds (${price:N0})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates ContractData on SEPARATE entity (not on vanilla building!).
        /// This avoids homeless spike cascade from archetype migration.
        /// </summary>
        private void DrainResolvedContractPayments(EntityCommandBuffer ecb)
        {
            if (m_ResolvedContractPaymentQuery.IsEmpty)
                return;

            foreach (var (intent, result, budgetEntity) in
                SystemAPI.Query<RefRW<ContractPaymentIntent>, RefRO<BudgetDeductResult>>()
                .WithAll<BudgetDeductRequest>()
                .WithEntityAccess())
            {
                var intentValue = intent.ValueRO;
                var buildingEntity = intentValue.GetBuilding();
                if (!result.ValueRO.Succeeded)
                {
                    Log.Warn($"Contract payment denied for entity {intentValue.Building.Index}, price: ${intentValue.Price:N0}");
                    EmitContractPaymentResult(ecb, intentValue, RequestStatus.Failed, ReasonIds.ContractInsufficientFunds);
                    ecb.DestroyEntity(budgetEntity);
                    continue;
                }

                if (!m_BuildingLookup.HasComponent(buildingEntity) || m_DeletedLookup.HasComponent(buildingEntity))
                {
                    if (!DrainOrQueueContractRefund(ecb, ref intent.ValueRW, result.ValueRO.Amount))
                        continue;

                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-stale-building");
                    Log.Warn($"Contract payment refunded for deleted entity {intentValue.Building.Index}, amount: ${result.ValueRO.Amount:N0}");
                    EmitContractPaymentResult(ecb, intent.ValueRO, RequestStatus.Failed, ReasonIds.ContractOfferStale);
                    ecb.DestroyEntity(budgetEntity);
                    continue;
                }

                if (HasLiveContractForBuilding(buildingEntity))
                {
                    if (!DrainOrQueueContractRefund(ecb, ref intent.ValueRW, result.ValueRO.Amount))
                        continue;

                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-duplicate-live-contract");
                    EmitContractPaymentResult(ecb, intent.ValueRO, RequestStatus.Failed, ReasonIds.ContractOfferStale);
                    Log.Warn($"Contract payment refunded for entity {intentValue.Building.Index}: live contract already exists");
                    ecb.DestroyEntity(budgetEntity);
                    continue;
                }

                if (!CreateContract(buildingEntity, intentValue, ecb))
                {
                    if (!DrainOrQueueContractRefund(ecb, ref intent.ValueRW, result.ValueRO.Amount))
                        continue;

                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-create-failed");
                    EmitContractPaymentResult(ecb, intent.ValueRO, RequestStatus.Failed, ReasonIds.InternalError);
                    ecb.DestroyEntity(budgetEntity);
                    continue;
                }
                if (intentValue.IsShady)
                    m_ReputationService.OnOfferAccepted();
                else
                    m_ReputationService.OnOfferRejected();
                if (intentValue.IsShady && intentValue.KickbackAmount > 0)
                    AddKickbackToWallet(intentValue.KickbackAmount, intentValue, ecb);

                EmitContractPaymentResult(ecb, intentValue, RequestStatus.Success, ReasonId.None);
                Log.Info($"{(intentValue.IsShady ? "SHADY" : "Official")} {intentValue.Type} contract signed for entity {intentValue.Building.Index}, price: ${intentValue.Price:N0}");
                ecb.DestroyEntity(budgetEntity);
            }
        }

        private bool DrainOrQueueContractRefund(EntityCommandBuffer ecb, ref ContractPaymentIntent intent, long amount)
        {
            if (amount <= 0)
                return true;

            string operationKey = ResolveContractRefundOperationKey(ref intent);
            if (intent.RefundResolved)
            {
                if (TryCleanupContractRefundResult(ecb, operationKey, ref intent))
                    return false;

                return true;
            }

            foreach (var (requestRef, resultRef, refundEntity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                intent.RefundQueued = true;
                intent.RefundResolved = true;
                intent.RefundSucceeded = resultRef.ValueRO.Succeeded;
                intent.RefundCleanupQueued = true;
                ecb.DestroyEntity(refundEntity);
                return false;
            }

            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                {
                    intent.RefundQueued = true;
                    return false;
                }
            }

            if (BudgetEmitter.TryQueueAddFunds(
                    ecb,
                    amount,
                    BudgetSource.ContractRefund,
                    BudgetIncomeKind.Refund,
                    operationKey,
                    out _,
                    BudgetResultMode.RetainResult))
            {
                intent.RefundQueued = true;
            }

            return false;
        }

        private bool TryCleanupContractRefundResult(EntityCommandBuffer ecb, string operationKey, ref ContractPaymentIntent intent)
        {
            foreach (var (requestRef, _, refundEntity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                ecb.DestroyEntity(refundEntity);
                intent.RefundCleanupQueued = true;
                return true;
            }

            intent.RefundCleanupQueued = false;
            return false;
        }

        private void EmitContractPaymentResult(EntityCommandBuffer ecb, in ContractPaymentIntent intent, RequestStatus status, ReasonId reasonId)
        {
            if (intent.RequestId <= 0)
                return;

            // RequestResultEvent is terminal-only — never emit Pending here.
            // Callers route Pending elsewhere; defensive guard documents the
            // contract for CIVIC428.
            if (status == RequestStatus.Pending)
                return;

            RequestResultEmitter.Emit(
                ecb,
                intent.RequestId,
                RequestKind.MaintenanceContract,
                status,
                reasonId,
                SystemAPI.Time.ElapsedTime,
                discriminatorKind: "offerKey",
                discriminatorValue: $"{intent.Building.Index}:{intent.Building.Version}");
        }

        private bool CreateContract(Entity buildingEntity, ContractPaymentIntent intent, EntityCommandBuffer ecb)
        {
            if (!TryGetCurrentGameDay(out var currentDay))
            {
                Log.Warn($"Contract payment deferred/refunded for entity {buildingEntity.Index}: GameTimeSystem unavailable");
                return false;
            }

            int durationDays = BalanceConfig.Current.Procurement.ContractDurationDays;
            if (durationDays <= 0)
            {
                Log.Error($"Invalid contract duration {durationDays}; refusing to create contract for entity {buildingEntity.Index}");
                return false;
            }

            // Create SEPARATE entity with ContractData (NEVER AddComponent on vanilla!)
            var contractEntity = ecb.CreateEntity();
            ecb.AddComponent(contractEntity, new ContractData
            {
                Building = BuildingRef.FromEntity(buildingEntity),
                Service = intent.Service,
                Type = intent.Type,
                Quality = intent.Quality,
                KickbackAmount = intent.KickbackAmount,
                ContractStartDay = currentDay,
                ContractDurationDays = durationDays,
                IsShady = intent.IsShady,
                VendorNameHash = intent.VendorNameHash
            });

            // Publish event for narrative system
            EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.ContractSigned,
                BuildingIndex: buildingEntity.Index,
                IsCorrupt: intent.IsShady,
                Cost: intent.Price,
                KickbackAmount: intent.KickbackAmount,
                ContractType: intent.Type.ToString()
            ), "ContractResponseSystem");

            return true;
        }

        private bool HasLiveContractForBuilding(Entity buildingEntity)
        {
            bool hasCurrentDay = TryGetCurrentGameDay(out var currentDay);

            foreach (var contractRef in SystemAPI.Query<RefRO<ContractData>>().WithNone<Deleted>())
            {
                var contract = contractRef.ValueRO;
                if (contract.Building.Index != buildingEntity.Index
                    || contract.Building.Version != buildingEntity.Version)
                    continue;

                if (!hasCurrentDay)
                    return true;

                int expirationDay = contract.ContractStartDay + contract.ContractDurationDays;
                if (currentDay < expirationDay)
                    return true;
            }

            return false;
        }

        private static string ContractRefundOperationKey(in ContractPaymentIntent intent)
            => ContractRefundOperationKey(intent.RequestId, intent.RequestCreatedFrame, intent.RequestCreatedTime);

        private static string ContractRefundOperationKey(int requestId, uint requestCreatedFrame, double requestCreatedTime)
            => $"ContractRefund:{requestId}:{requestCreatedFrame}:{requestCreatedTime:R}";

        private static string ResolveContractRefundOperationKey(ref ContractPaymentIntent intent)
        {
            string stored = intent.RefundOperationKey.ToString();
            if (!string.IsNullOrEmpty(stored))
                return stored;

            string fallback = ContractRefundOperationKey(in intent);
            intent.RefundOperationKey = new FixedString128Bytes(fallback);
            return fallback;
        }

        /// <summary>
        /// Adds kickback amount via ShadowIncomeRequest (single-writer pattern).
        /// </summary>
        private void AddKickbackToWallet(int amount, in ContractPaymentIntent intent, EntityCommandBuffer ecb)
        {
            string incomeKey = $"ContractKickback:{intent.RequestId}:{intent.Building.Index}:{intent.Type}";
            if (ShadowEconomyEmitter.TryQueueIncome(World, ecb, amount, "ContractKickback", incomeKey) && Log.IsDebugEnabled)
                Log.Debug($"Created ShadowIncomeRequest for ${amount:N0} kickback");
        }

        private static bool TryGetCurrentGameDay(out int currentDay)
            => GameTimeSystem.TryGetDay(out currentDay);

        protected override void OnDestroy()
        {
            Log.Info("Destroyed");
            base.OnDestroy();
        }
    }
}
