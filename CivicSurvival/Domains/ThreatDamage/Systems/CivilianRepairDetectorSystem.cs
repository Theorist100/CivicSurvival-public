using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Domain.ThreatDamage;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Pause-safe ingress for civilian repair requests. Converts transient UI
    /// requests into durable two-phase intents in ModificationEnd.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.CivilianRepair)]
    [TransientConsumerReconcile(typeof(CivilianRepairRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: durable CivilianRepairIntent is created by this ModificationEnd consumer, so pre-consume load loss is reissuable.")]
    public partial class CivilianRepairDetectorSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("CivilianRepairDetectorSystem");

        private EntityQuery m_RequestQuery;
        private EntityQuery m_WaveStateQuery;
        private ModificationEndBarrier m_Barrier = null!;
#pragma warning disable CIVIC229 // System reference — same-domain owner/read service.
        private CivilianDamageSystem m_DamageSystem = null!;
#pragma warning restore CIVIC229
        private CivicDependencyWire m_DependencyWire = null!;
        private readonly System.Collections.Generic.HashSet<long> m_InFrameTargets = new();
        [System.NonSerialized] private int m_InFrameTargetsFrame = -1;

#pragma warning disable CIVIC241, CIVIC312 // Ephemeral id cursor; AllocateRepairId rescans surviving intents before issuing a new id.
        [System.NonSerialized] private int m_NextRepairId = 1;
#pragma warning restore CIVIC241, CIVIC312

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<CivilianRepairRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_DependencyWire = new CivicDependencyWire(nameof(CivilianRepairDetectorSystem));
            RequireForUpdate(m_RequestQuery);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_DamageSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<CivilianDamageSystem>());
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty)
                return;

            int frame = UnityEngine.Time.frameCount;
            if (frame != m_InFrameTargetsFrame)
            {
                m_InFrameTargets.Clear();
                m_InFrameTargetsFrame = frame;
            }

            // Request entity is destroyed synchronously via EntityManager so duplicate
            // ticks at >1x sim speed see an empty query (no per-frame dedup state needed).
            // The ModificationEndBarrier ECB stays for the *intent* entity creation and
            // result emission (intent flows into CivilianRepairCommitSystem on a later
            // tick). Audit-verified: no scheduled jobs read CivilianRepairRequest, so
            // the sync destroy creates no sync point.
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<CivilianRepairRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!ecbCreated)
                {
                    ecb = m_Barrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                if (!TryCreateIntent(ecb, request.ValueRO, meta.ValueRO, out var failureReason))
                    EmitFailed(ecb, meta.ValueRO.RequestId, failureReason);

#pragma warning disable CIVIC006, CIVIC208 // Single-shot UI command consumer: no scheduled jobs read CivilianRepairRequest (audit-verified), so synchronous destroy creates no sync point.
                EntityManager.DestroyEntity(entity);
#pragma warning restore CIVIC006, CIVIC208
            }

            if (ecbCreated)
                m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private bool TryCreateIntent(
            EntityCommandBuffer ecb,
            in CivilianRepairRequest request,
            in RequestMeta meta,
            out ReasonId failureReason)
        {
            var gate = ActionGate.Resolve(ActionKey.CivilianRepair, BuildActionContext());
            if (!gate.CanRun)
            {
                failureReason = ReasonId.FromRuntime(gate.LockedReasonId);
                return false;
            }

            if (!Enum.IsDefined(typeof(RepairType), request.RepairType))
            {
                failureReason = ReasonIds.InvalidRepairType;
                return false;
            }

            ICivilianDamageReader reader = m_DamageSystem;
            var (found, view) = reader.GetRepairState(request.Building.Index, request.Building.Version);
            if (!found)
            {
                failureReason = ReasonIds.CivilianRepairNotFound;
                return false;
            }

            if (view.IsUnderRepair)
            {
                failureReason = ReasonIds.CivilianRepairAlreadyActive;
                return false;
            }

            long targetKey = BuildingIdentityKey.Pack(request.Building.Index, request.Building.Version);
            if (!m_InFrameTargets.Add(targetKey))
            {
                failureReason = ReasonIds.CivilianRepairBudgetPending;
                return false;
            }

            if (reader.HasPendingRepairIntent(request.Building.Index, request.Building.Version))
            {
                failureReason = ReasonIds.CivilianRepairBudgetPending;
                return false;
            }

            var repairParams = RepairPaymentHelper.CalculateCivilianRepairParams(view.HitCount, request.RepairType);
            if (repairParams.Cost <= 0 || repairParams.DurationHours <= 0f)
            {
                failureReason = ReasonIds.PlantRepairConfigError;
                return false;
            }

            // Affordability below is an optimistic pre-check — we intentionally do NOT
            // RegisterPendingDeduction here. The authoritative gate is CivilianRepairPaymentSystem,
            // which deducts synchronously via BudgetTransactionResolver.Deduct and cannot overdraw
            // (on insufficient funds it sets BudgetSucceeded=false, which the commit path rejects).
            // Two repairs started in the same frame may both pass this pre-check; the second then
            // fails cleanly at payment instead of overspending — no money-loss window. A pending
            // reservation would have to be released on every payment/commit/reject/dropped-intent
            // path (leak-prone) to close a window that carries no balance risk, so it is omitted by
            // design. (Contrast the async BudgetEmitter path, where reservation IS required.)
            int effectiveCost = repairParams.Cost;
            if (request.RepairType == RepairType.ShadowOps)
            {
                var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                var affordability = wallet.CanAffordWithPending(repairParams.Cost);
                if (!affordability.Affordable)
                {
                    failureReason = ReasonIds.InfraCivilianShadowInsufficient;
                    return false;
                }

                effectiveCost = (int)Math.Min(affordability.EffectiveCost, int.MaxValue);
            }
            else if (!CityBudgetService.CanAffordWithPending(World, repairParams.Cost))
            {
                failureReason = ReasonIds.InsufficientFunds;
                return false;
            }

            int kickback = 0;
            if (request.RepairType == RepairType.MunicipalWithKickback && repairParams.Kickback > 0)
            {
                var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                if (wallet.HasWallet && !wallet.IsFrozen)
                    kickback = repairParams.Kickback;
            }

            var intentEntity = ecb.CreateEntity();
            ecb.AddComponent(intentEntity, new CivilianRepairIntent
            {
                Building = request.Building,
                RepairId = AllocateRepairId(),
                Cost = effectiveCost,
                KickbackAmount = kickback,
                RepairTypeByte = (byte)request.RepairType,
                DurationHours = repairParams.DurationHours,
                RequestId = meta.RequestId
            });

            failureReason = ReasonId.None;
            Log.Info($"Civilian repair intent created: building={request.Building.Index}, type={request.RepairType}, cost={effectiveCost}");
            return true;
        }

        private ActionContext BuildActionContext()
        {
            bool hasWaveState = m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState);
            return new ActionContext(
                hasWaveState,
                hasWaveState ? waveState.CurrentPhase : GamePhase.Calm,
                false,
                Act.PreWar);
        }

        private int AllocateRepairId()
        {
            int maxExisting = 0;
            foreach (var intent in SystemAPI.Query<RefRO<CivilianRepairIntent>>())
                maxExisting = Math.Max(maxExisting, intent.ValueRO.RepairId);

            if (m_NextRepairId <= maxExisting)
                m_NextRepairId = maxExisting + 1;
            if (m_NextRepairId <= 0)
                m_NextRepairId = 1;
            if (m_NextRepairId == int.MaxValue)
                return int.MaxValue;
            return m_NextRepairId++;
        }

        private void EmitFailed(EntityCommandBuffer ecb, int requestId, ReasonId reasonId)
        {
            if (requestId == 0)
                return;

            var resultEntity = RequestResultEmitter.Emit(
                ecb,
                requestId,
                RequestKind.CivilianRepair,
                RequestStatus.Failed,
                reasonId,
                SystemAPI.Time.ElapsedTime);
            ecb.AddComponent<Reported>(resultEntity);
            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.CivilianRepair,
                requestId,
                RequestStatus.Failed,
                reasonId.ToString());
        }
    }
}
