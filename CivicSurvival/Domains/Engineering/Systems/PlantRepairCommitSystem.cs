using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Domain.Engineering;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Engineering.Services;
#pragma warning disable CIVIC182 // Phase-neutral budget refund helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// Pause-safe plant-repair commit. Applies resolved repair intents in
    /// ModificationEnd and destroys the transaction entity.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.PlantRepair)]
    public partial class PlantRepairCommitSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("PlantRepairCommitSystem");

        private EntityQuery m_IntentQuery;
        private EntityQuery m_WaveStateQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private ComponentLookup<EquipmentWear> m_EquipmentWearLookup;
        private ComponentLookup<ElectricityProducer> m_ElectricityProducerLookup;
        private ComponentLookup<PlantBaseCapacity> m_BaseCapacityLookup;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        [System.NonSerialized] private CivicServiceLookups m_RepairLookups = null!;
#pragma warning disable CIVIC229 // System references — calls owner APIs, no state ownership here.
        [System.NonSerialized] private PlantWearSimulation m_WearSim = null!;
        [System.NonSerialized] private PlantRepairRequestProcessor m_Processor = null!;
#pragma warning restore CIVIC229
        private IEquipmentUIService m_EquipmentUiService = NullEquipmentUIService.Instance;
        private IPowerCapacitySnapshotReader? m_PowerCapacitySnapshotReader;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<RepairTransactionIntent>());
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_EquipmentWearLookup = GetComponentLookup<EquipmentWear>(false);
            m_ElectricityProducerLookup = GetComponentLookup<ElectricityProducer>(true);
            m_BaseCapacityLookup = GetComponentLookup<PlantBaseCapacity>(true);
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_RepairLookups = new CivicServiceLookups(() =>
            {
                m_EquipmentWearLookup.Update(this);
                m_ElectricityProducerLookup.Update(this);
                m_BaseCapacityLookup.Update(this);
                m_StorageInfoLookup.Update(this);
                m_DeletedLookup.Update(this);
                m_DestroyedLookup.Update(this);
            });
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_WearSim ??= FeatureRegistry.Instance.Require<PlantWearSimulation>();
            m_Processor ??= FeatureRegistry.Instance.Require<PlantRepairRequestProcessor>();
            m_EquipmentUiService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullEquipmentUIService.Instance);
            m_PowerCapacitySnapshotReader ??= ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            m_WearSim ??= FeatureRegistry.Instance.Require<PlantWearSimulation>();
            m_Processor ??= FeatureRegistry.Instance.Require<PlantRepairRequestProcessor>();
            m_RepairLookups.RefreshIfStale();
            m_WearSim.RefreshPlantIdMap();

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<RepairTransactionIntent>>()
                .WithEntityAccess())
            {
                var intent = intentRef.ValueRO;
                if (intent.Applied || !intent.BudgetResolved)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                intentRef.ValueRW.Applied = true;
                if (!intent.BudgetSucceeded)
                {
                    RejectBudgetFailed(ecb, entity, intent);
                    continue;
                }

                if (!ApplyOrRefund(ecb, entity, intent))
                    intentRef.ValueRW.Applied = false;
            }

            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool ApplyOrRefund(EntityCommandBuffer ecb, Entity intentEntity, RepairTransactionIntent intent)
        {
            var repairCtx = BuildRepairContext(ecb);
            var (targetEntity, wear) = PlantRepairService.FindPlantByStableId(ref repairCtx, intent.PlantId);
            bool committedRepair = false;
            string terminalReasonId = "";

            if (targetEntity != Entity.Null)
            {
                if (wear.IsUnderRepair)
                {
                    // Post-load replay: durable wear sidecar already shows the repair
                    // as in-progress. Treat as committed and emit the same Success
                    // terminal as the fresh-apply branch. Requeue the same idempotent
                    // kickback request before terminal success so save/load recovery
                    // preserves the shadow-wallet side effect.
                    if (!ApplyKickback(ecb, intent))
                    {
                        Log.Warn($"Repair replay for plant {intent.PlantId} deferred: kickback request could not be queued");
                        return false;
                    }

                    committedRepair = true;
                    m_EquipmentUiService.MarkPlantsDirty();

                    // Defensive observability: in the steady-state (non-load) path the
                    // intake guard prevents a duplicate intent from ever reaching commit
                    // for a plant that is already under repair. If we DO see this branch
                    // while the intent paid its own cost (BudgetSucceeded), surface it as
                    // a warning so a duplicate-intent regression cannot silently suppress
                    // a refund. The dominant case is a genuine post-load replay where the
                    // prior session already deducted the cost and started the repair, so
                    // the Success-without-refund emit below is correct.
                    if (intent.BudgetSucceeded && intent.Cost > 0)
                    {
                        Log.Warn($"Plant {intent.PlantId} already under repair AND intent BudgetSucceeded — " +
                                 $"replay branch suppressed refund (cost={intent.Cost}); expected only for post-load replay, " +
                                 $"suspicious in steady-state.");
                    }
                    else
                    {
                        Log.Info($"Repair replay confirmed for plant {intent.PlantId} (already under repair post-load)");
                    }
                }
                else if (PlantRepairService.ApplyRepairToEntity(ref repairCtx, targetEntity, ref wear, intent.DurationHours))
                {
                    if (!ApplyKickback(ecb, intent))
                    {
                        m_EquipmentUiService.MarkPlantsDirty();
                        Log.Warn($"Repair confirmation for plant {intent.PlantId} deferred: kickback request could not be queued");
                        return false;
                    }

                    committedRepair = true;
                    m_EquipmentUiService.MarkPlantsDirty();
                    Log.Info($"Repair confirmed for plant {intent.PlantId}, duration {intent.DurationHours}h");
                }
                else
                {
                    bool refunded = RefundRepairPayment(ecb, intent);
                    terminalReasonId = refunded
                        ? ReasonIds.RepairRejected
                        : ReasonIds.PlantRepairRefundFailed;
                }
            }
            else
            {
                bool refunded = RefundRepairPayment(ecb, intent);
                terminalReasonId = refunded
                    ? ReasonIds.PlantRepairNotFound
                    : ReasonIds.PlantRepairRefundFailed;
            }

            if (committedRepair)
            {
                if (intent.RequestId != 0)
                    EmitTerminal(ecb, intent.RequestId, RequestStatus.Success, ReasonId.None);
            }
            else if (intent.RequestId != 0)
            {
                EmitTerminal(ecb, intent.RequestId, RequestStatus.Failed, ReasonId.FromRuntime(terminalReasonId));
            }

            m_Processor.MarkResolved(intent.PlantId);
            ecb.DestroyEntity(intentEntity);
            return true;
        }

        private void RejectBudgetFailed(EntityCommandBuffer ecb, Entity intentEntity, RepairTransactionIntent intent)
        {
            Log.Warn($"Repair budget failed for plant {intent.PlantId} - no repair started");
            EventBus?.SafePublish(
                new ThreatNarrativeEvent(ThreatNarrativeEventType.RepairNoFunds),
                "PlantRepairCommitSystem");

            if (intent.RequestId != 0)
                EmitTerminal(ecb, intent.RequestId, RequestStatus.Failed, ReasonIds.RepairRejected);

            m_Processor.MarkResolved(intent.PlantId);
            ecb.DestroyEntity(intentEntity);
        }

        private bool ApplyKickback(EntityCommandBuffer ecb, RepairTransactionIntent intent)
        {
            if (intent.RepairType != RepairType.MunicipalWithKickback || intent.KickbackAmount <= 0)
                return true;

            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            if (!wallet.IsOperational || wallet.IsFrozen)
            {
                Log.Warn($"Repair kickback skipped for plant {intent.PlantId}: shadow wallet unavailable");
                return true;
            }

            bool queued = ShadowEconomyEmitter.TryQueueIncome(
                World,
                ecb,
                intent.KickbackAmount,
                $"RepairKickback:{intent.PlantId}",
                $"RepairKickback:{intent.RequestId}:{intent.PlantId}");
            if (!queued)
                return false;

#pragma warning disable CIVIC062 // One-shot event per confirmed repair, not per-frame.
            var cfg = BalanceConfig.Current.InfrastructureRepair;
            EventBus?.SafePublish(
                new CorruptionGainEvent(cfg.KickbackCorruptionExposure, "RepairKickback"),
                "PlantRepairCommitSystem");
#pragma warning restore CIVIC062
            return true;
        }

        private bool RefundRepairPayment(EntityCommandBuffer ecb, RepairTransactionIntent intent)
        {
            if (intent.Cost <= 0)
                return true;

            bool isShadow = intent.RepairType == RepairType.ShadowOps;
            bool refunded = isShadow
                ? ShadowEconomyEmitter.TryApplyRefund(
                    World,
                    intent.Cost,
                    $"RepairRefund:{intent.PlantId}",
                    $"RepairRefund:{intent.RequestId}:{intent.PlantId}")
                : BudgetTransactionResolver.QueueRefund(ecb, intent.Cost, BudgetSource.RepairRefund, BudgetIncomeKind.Refund);

            if (refunded)
                Log.Warn($"Plant repair refund: plant {intent.PlantId} unavailable during commit, refunded {intent.Cost} ({(isShadow ? "shadow" : "municipal")})");
            else
                Log.Error($"Plant repair refund FAILED: plant {intent.PlantId}, amount {intent.Cost} ({(isShadow ? "shadow" : "municipal")})");
            return refunded;
        }

        private PlantRepairContext BuildRepairContext(EntityCommandBuffer ecb)
        {
            var wavePhase = (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws)
                    ? ws : WaveStateSingleton.Default)
                .CurrentPhase;

            double gameHour = GameTimeSystem.Instance != null
                ? GameTimeSystem.Instance.Current.TotalGameHours
                : 0f;

            return new PlantRepairContext
            {
                GameHour = (float)gameHour,
                World = World,
                EventBus = EventBus,
                PlantIdToEntity = m_WearSim.PlantIdToEntity,
                WearLookup = m_EquipmentWearLookup,
                ProducerLookup = m_ElectricityProducerLookup,
                BaseCapacityLookup = m_BaseCapacityLookup,
                StorageInfoLookup = m_StorageInfoLookup,
                DeletedLookup = m_DeletedLookup,
                DestroyedLookup = m_DestroyedLookup,
                OperationalDamageRepairSink = ServiceRegistry.IsInitialized
                    ? ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullOperationalDamageRepairSink.Instance)
                    : null,
                DisasterRepairSink = ServiceRegistry.IsInitialized
                    ? ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDisasterRepairSink.Instance)
                    : null,
                PowerCapacitySnapshotReader = m_PowerCapacitySnapshotReader,
                Ecb = ecb,
                CurrentPhase = wavePhase
            };
        }

        private void EmitTerminal(EntityCommandBuffer ecb, int requestId, RequestStatus status, ReasonId reasonId)
        {
            var resultEntity = status == RequestStatus.Success
                ? RequestResultEmitter.EmitSuccess(
                    ecb,
                    requestId,
                    RequestKind.PlantRepair,
                    SystemAPI.Time.ElapsedTime)
                : RequestResultEmitter.Emit(
                    ecb,
                    requestId,
                    RequestKind.PlantRepair,
                    status,
                    reasonId,
                    SystemAPI.Time.ElapsedTime);
            ecb.AddComponent<Reported>(resultEntity);
            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.PlantRepair,
                requestId,
                status,
                reasonId.ToString());
        }
    }
}
