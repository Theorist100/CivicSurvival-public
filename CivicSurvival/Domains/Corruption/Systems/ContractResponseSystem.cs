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
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Processes ContractResponse ephemeral entities on the pause-safe
    /// ModificationEnd route (Axiom 14).
    ///
    /// Flow (single tick, terminal by construction):
    /// 1. UI creates a ContractResponse entity via EndFrameBarrier (works while paused).
    /// 2. This system validates the offer, runs every preflight check BEFORE money moves,
    ///    then pays synchronously through BudgetTransactionResolver (same path as
    ///    plant/civilian repairs) and creates the ServiceContract in the same tick.
    /// 3. Terminal RequestResult is emitted immediately; the request entity is destroyed.
    ///
    /// Payment is synchronous, so no refund/intent machinery exists: a failed check
    /// rejects before the deduct, and a successful deduct always yields a contract.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.MaintenanceContract)]
    [TransientConsumerReconcile(typeof(ContractResponse), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command consumed in one ModificationEnd tick with synchronous payment: a save in the one-frame in-flight window loses only the click; the durable PendingProcurement offer stays Active and the player can respond again.")]
    public partial class ContractResponseSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ContractResponseSystem");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private IShadowReputationService m_ReputationService = null!;
        private IShadowWalletService m_WalletService = null!;
        private MaintenanceContractSystem? m_MaintenanceContracts;
        private ComponentLookup<PendingProcurement> m_PendingProcurementLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<ContractResponse>()
            );

            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_PendingProcurementLookup = GetComponentLookup<PendingProcurement>(false);
            m_BuildingLookup = GetComponentLookup<Building>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);

            Log.Info("Created (synchronous payment, pause-safe)");
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
            if (m_RequestQuery.IsEmpty)
                return;

            m_PendingProcurementLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_DeletedLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            foreach (var (request, meta, requestEntity) in
                SystemAPI.Query<RefRO<ContractResponse>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                var result = ProcessRequest(request.ValueRO, meta.ValueRO, ecb);
                // Every outcome is terminal: payment is synchronous, so there is no
                // pending window. Pending here would be a producer bug — guard
                // defensively and document so CIVIC428 sees the terminal-only intent.
                if (result.Status != RequestStatus.Pending)
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.MaintenanceContract, result.Status, result.ReasonId, SystemAPI.Time.ElapsedTime);
                ecb.DestroyEntity(requestEntity);
            }

            if (hasEcb)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private readonly struct ContractRequestResult
        {
            public readonly RequestStatus Status;
            public readonly ReasonId ReasonId;

            private ContractRequestResult(RequestStatus status, ReasonId reasonId)
            {
                Status = status;
                ReasonId = reasonId;
            }

            public static ContractRequestResult Terminal(RequestStatus status, ReasonId reasonId) => new(status, reasonId);
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
                    return AcceptContract(buildingEntity, offer, isShady: false, request.ExpectedPrice, meta.RequestId, ecb);

                case ContractResponseType.AcceptShady:
                    var shadyGate = ActionGate.Resolve(
                        ActionKey.ShadyContractAccept,
                        BuildShadowActionContext(proposedCost: 0));
                    if (!shadyGate.CanRun)
                    {
                        Log.Info($"Shady contract blocked for entity {entityIndex}: {shadyGate.LockedReasonId}");
                        return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonId.FromRuntime(shadyGate.LockedReasonId));
                    }
                    return AcceptContract(buildingEntity, offer, isShady: true, request.ExpectedPrice, meta.RequestId, ecb);

                case ContractResponseType.Decline:
                    MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "declined");
                    m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                    m_ReputationService.OnOfferRejected();
                    EventBus?.SafePublish(new ShadowNarrativeEvent(
                        ShadowNarrativeEventType.OfferDeclined,
                        BuildingIndex: buildingEntity.Index,
                        // CIVIC061: branch instead of enum.ToString() — two known values.
                        ContractType: offer.Type == ContractType.Supply ? nameof(ContractType.Supply) : nameof(ContractType.Maintenance)), "ContractResponseSystem");
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

        /// <summary>
        /// Accept an offer with a synchronous payment. Every rejection happens BEFORE
        /// the deduct, so money never moves on a failed acceptance and no refund path
        /// is needed. A successful deduct always produces the contract in the same tick.
        /// </summary>
        private ContractRequestResult AcceptContract(Entity buildingEntity, PendingProcurement offer, bool isShady, long expectedPrice, int requestId, EntityCommandBuffer ecb)
        {
            long price = isShady ? offer.ShadyPrice : offer.OfficialPrice;

            if (expectedPrice != price)
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "price-mismatch");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: expected ${expectedPrice:N0}, current ${price:N0}");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.ProcurementPriceChanged);
            }

            if (HasLiveContractForBuilding(buildingEntity))
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "duplicate-live-contract");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: live contract already exists");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.ContractOfferStale);
            }

            if (!CanPayContract(buildingEntity, price, out var preflightReason))
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-preflight-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonId.FromRuntime(preflightReason.ToString()));
            }

            if (!TryGetCurrentGameDay(out var currentDay))
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-preflight-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: GameTimeSystem unavailable");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.InternalError);
            }

            int durationDays = BalanceConfig.Current.Procurement.ContractDurationDays;
            if (durationDays <= 0)
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-preflight-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Error($"Invalid contract duration {durationDays}; refusing to create contract for entity {buildingEntity.Index}");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.InternalError);
            }

            // Synchronous deduct through the shared main-thread resolver — the same
            // pause-safe payment path plant/civilian repairs use.
            var deductResult = BudgetTransactionResolver.Deduct(
                World,
                m_WalletService,
                price,
                BudgetCategory.Procurement,
                $"MaintenanceContract:{buildingEntity.Index}:{buildingEntity.Version}");
            if (!deductResult.Succeeded)
            {
                MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "payment-failed");
                m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
                Log.Warn($"Contract payment rejected for entity {buildingEntity.Index}: deduct failed (${price:N0})");
                return ContractRequestResult.Terminal(RequestStatus.Failed, ReasonIds.ContractInsufficientFunds);
            }

            int kickback = isShady ? offer.KickbackOffer : 0;
            CreateContract(buildingEntity, offer, isShady, price, kickback, currentDay, durationDays, ecb);

            if (isShady)
                m_ReputationService.OnOfferAccepted();
            else
                m_ReputationService.OnOfferRejected();
            if (kickback > 0)
                AddKickbackToWallet(kickback, requestId, buildingEntity, offer.Type, ecb);

            MaintenanceContractSystem.ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, buildingEntity, "accepted-paid");
            m_MaintenanceContracts?.RecordPendingOfferCleared(buildingEntity);
            Log.Info($"{(isShady ? "SHADY" : "Official")} {offer.Type} contract signed for entity {buildingEntity.Index}, price: ${price:N0}" + (kickback > 0 ? $", kickback: ${kickback:N0}" : ""));
            return ContractRequestResult.Terminal(RequestStatus.Success, ReasonId.None);
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

        private bool CanPayContract(Entity buildingEntity, long price, out FixedString64Bytes failReason)
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
        /// All preconditions (game day, duration, funds) are validated by the caller
        /// before the deduct, so contract creation cannot fail after money moved.
        /// </summary>
        private void CreateContract(Entity buildingEntity, in PendingProcurement offer, bool isShady, long price, int kickback, int currentDay, int durationDays, EntityCommandBuffer ecb)
        {
            var contractEntity = ecb.CreateEntity();
            ecb.AddComponent(contractEntity, new ContractData
            {
                Building = BuildingRef.FromEntity(buildingEntity),
                Service = offer.Service,
                Type = offer.Type,
                Quality = isShady ? offer.ShadyQuality : offer.OfficialQuality,
                KickbackAmount = kickback,
                ContractStartDay = currentDay,
                ContractDurationDays = durationDays,
                IsShady = isShady,
                VendorNameHash = isShady ? offer.ShadyVendorHash : offer.OfficialVendorHash
            });

            // Publish event for narrative system
            EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.ContractSigned,
                BuildingIndex: buildingEntity.Index,
                IsCorrupt: isShady,
                Cost: price,
                KickbackAmount: kickback,
                ContractType: offer.Type.ToString()
            ), "ContractResponseSystem");
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

        /// <summary>
        /// Adds kickback amount via ShadowIncomeRequest (single-writer pattern).
        /// The request is durable (ISerializable, keyed) and drains when the
        /// simulation ticks — while paused it settles on unpause.
        /// </summary>
        private void AddKickbackToWallet(int amount, int requestId, Entity buildingEntity, ContractType contractType, EntityCommandBuffer ecb)
        {
            string incomeKey = $"ContractKickback:{requestId}:{buildingEntity.Index}:{contractType}";
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
