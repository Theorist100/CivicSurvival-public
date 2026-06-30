using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Game;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.GridWarfare;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;
using CivicSurvival.Domains.GridWarfare.Data;
using CivicSurvival.Domains.GridWarfare.Events;
using CivicSurvival.Domains.GridWarfare.Systems;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using static CivicSurvival.Core.UI.B;
using OperationStateEnum = CivicSurvival.Domains.GridWarfare.Data.OperationState;

namespace CivicSurvival.Domains.GridWarfare.UI
{
    /// <summary>
    /// UI system for GridWarfare.
    /// Bindings for enemy state, player operations, stability.
    ///
    /// Migrated from GridWarfareUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.OperationLaunch)]
    public partial class GridWarfareUISystem : CivicUIPanelSystem
    {
        private PlayerAttackSystem m_PlayerSystem = null!;
        private CityStabilitySystem m_StabilitySystem = null!;
        private ICounterAttackArsenalService m_Arsenal = null!;

        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_WalletQuery;

        // PERF: Cached JSON
        private string m_CachedOperationSlotsJson = JsonBuilder.EmptyArray;
        private Dictionary<string, long> m_CachedAttackCosts = BuildAttackCosts(0f, 0f);
        // m_LastSlotCount removed: #464 fix — always rebuild JSON for progress accuracy
        private float m_LastDiscount = -1f;
        private float m_LastSanctionsMarkup = -1f; // FIX S13-03
        private bool m_LastStabilityEnabled; // F14: track enabled-state transitions for cache invalidation
        private readonly OperationSlotSnapshot[] m_SlotSnapshot = new OperationSlotSnapshot[3];
        private int m_SlotObserverCursor = int.MinValue;

        // Event-driven: phase change toasts
        private NotificationState? m_Notifications;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadOnly<EnemyState>());
            m_WalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());

            // Subscribe early, then buffer delivery until OnStartRunning resolves dependencies.
            SubscribeBufferedUntilReady<OperationPreparingEvent>(OnOperationPreparing);
            SubscribeBufferedUntilReady<OperationReadyEvent>(OnOperationReady);
            SubscribeBufferedUntilReady<OperationCancelledEvent>(OnOperationCancelled);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Notifications ??= ServiceRegistry.Instance.Require<NotificationState>();
            m_PlayerSystem ??= FeatureRegistry.Instance.Require<PlayerAttackSystem>();
            m_StabilitySystem ??= FeatureRegistry.Instance.Require<CityStabilitySystem>();
            // Same-domain arsenal stock for the War Room readout. Fail-closed null-object
            // returns 0 stock when the arsenal singleton is not yet live (CIVIC403).
            m_Arsenal ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCounterAttackArsenalService.Instance);
            MarkEventHandlersReady();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(GridWarfareState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            // Grid operations are player UI commands. Apply them synchronously through
            // PlayerAttackSystem's locked API so clicking "blackout" while paused
            // reserves/refunds/executes immediately instead of waiting for GameSimulation.
            Triggers.Add<string>(PrepareOperation, FeatureIds.GridWarfare, RequestResultBridge.GridOperation, OnPrepareOperation);
            Triggers.Add<string>(ExecuteOperation, FeatureIds.GridWarfare, RequestResultBridge.GridOperation, OnExecuteOperation);
            Triggers.Add<string>(CancelOperation, FeatureIds.GridWarfare, RequestResultBridge.GridOperation, OnCancelOperation);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new GridWarfareDto
            {
                CityStability = 100f,
                StabilityDiscount = 0f,
                AttackCosts = m_CachedAttackCosts,
                OperationRequestJson = RequestResultBridge.Get(RequestResultBridge.GridOperation).ToJson()
            };

            FillWallet(ref dto);
            float gameTimeHours = GetGameTimeHours();
            float gameTimeSeconds = gameTimeHours * GameRate.SECONDS_PER_HOUR;

            FillEnemy(ref dto, gameTimeHours);
            FillArsenalStock(ref dto);
            FillStability(ref dto);
            FillOperationSlots(ref dto, gameTimeSeconds);
            FillAttackCosts(ref dto);
            FillOperationEligibility(ref dto);
            FillUnlockState(ref dto);

            PublishWhenComplete(GridWarfareState, NoSourceChecks, () => dto);
        }

        private void FillWallet(ref GridWarfareDto dto)
        {
            if (!m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)) return;

            dto.ShadowBalance = (int)Math.Clamp(wallet.Balance, int.MinValue, int.MaxValue);
            dto.ShadowLocked = (int)Math.Clamp(wallet.LockedBalance, int.MinValue, int.MaxValue);
            dto.ShadowTotal = (int)Math.Clamp(wallet.GetTotalBalance(), int.MinValue, int.MaxValue);
        }

        private void FillEnemy(ref GridWarfareDto dto, float gameTimeHours)
        {
            if (!m_EnemyStateQuery.TryGetSingleton<EnemyState>(out var state)) return;

            dto.EnemyPhysicalAxis = state.PhysicalAxis;
            dto.EnemyDigitalAxis = state.DigitalAxis;
            dto.EnemySocialAxis = state.SocialAxis;
            dto.EnemyInterceptChance = state.InterceptChance;

            // Respite + act-objective readout (Phase 3.6.3). Active-respite flags drive the per-axis
            // "Suppressed" badge; ObjectiveProgress is the worst axis's normalized distance from full
            // health toward the objective threshold, so it reaches 1 only when ALL three are floored
            // enough to trigger the beachhead-collapse loot.
            var gw = BalanceConfig.Current.GridWarfare;
            dto.RespitePhysicalActive = state.IsRespiteActive(AttackCategory.Kinetic, gameTimeHours);
            dto.RespiteDigitalActive = state.IsRespiteActive(AttackCategory.Cyber, gameTimeHours);
            dto.RespiteSocialActive = state.IsRespiteActive(AttackCategory.Psyops, gameTimeHours);

            float cap = gw.PressureCap;
            float threshold = gw.ObjectiveAxisThreshold;
            float denom = math.max(cap - threshold, 1f);
            float pPhysical = math.clamp((cap - state.PhysicalAxis) / denom, 0f, 1f);
            float pDigital = math.clamp((cap - state.DigitalAxis) / denom, 0f, 1f);
            float pSocial = math.clamp((cap - state.SocialAxis) / denom, 0f, 1f);
            dto.ObjectiveProgress = math.min(pPhysical, math.min(pDigital, pSocial));
        }

        private void FillArsenalStock(ref GridWarfareDto dto)
        {
            // Null-object service reports IsAvailable=false and StockOf=0 — readout
            // shows zero stock until the arsenal singleton is live (fail-closed).
            if (!m_Arsenal.IsAvailable) return;

            dto.DroneStock = m_Arsenal.StockOf(ArsenalKind.Drone);
            dto.BallisticStock = m_Arsenal.StockOf(ArsenalKind.Ballistic);
        }

        private void FillStability(ref GridWarfareDto dto)
        {
            if (m_StabilitySystem == null || !m_StabilitySystem.Enabled) return;

            dto.CityStability = m_StabilitySystem.StabilityPercent;
            dto.StabilityDiscount = m_PlayerSystem != null && m_PlayerSystem.Enabled ? m_PlayerSystem.StabilityDiscount : 0f;
        }

        private void FillOperationSlots(ref GridWarfareDto dto, float gameTime)
        {
            if (m_PlayerSystem == null || !m_PlayerSystem.Enabled)
            {
                dto.OperationSlotsJson = JsonBuilder.EmptyArray;
                return;
            }

            var observed = m_PlayerSystem.SlotsView.Observe(ref m_SlotObserverCursor);
            if (observed.Changed)
            {
                observed.Value.CopySlotsTo(m_SlotSnapshot, Math.Min(m_SlotSnapshot.Length, observed.Value.SlotCount));
            }

            // #464 FIX: Always rebuild JSON instead of count-only check
            // Slots are max 3 — serialization cost is negligible vs stale progress bars
            int activeCount = 0;
            foreach (var slot in m_SlotSnapshot)
            {
                if (slot.State != (int)OperationStateEnum.Idle) activeCount++;
            }

            if (activeCount == 0)
            {
                m_CachedOperationSlotsJson = JsonBuilder.EmptyArray;
            }
            else
            {
                var sb = new StringBuilder(256);
                sb.Append('[');
                bool first = true;
                foreach (var slot in m_SlotSnapshot)
                {
                    if (slot.State == (int)OperationStateEnum.Idle) continue;

                    var entry = new OperationSlotDto
                    {
                        AttackType = slot.AttackType,
                        OperationState = EnumName<OperationStateEnum>.Lower((OperationStateEnum)slot.State),
                        Cost = slot.LockedAmount,
                        Progress = slot.GetProgress(gameTime),
                    };
                    if (!first) sb.Append(',');
                    first = false;
                    entry.WriteTo(sb);
                }
                sb.Append(']');
                m_CachedOperationSlotsJson = sb.ToString();
            }

            dto.OperationSlotsJson = m_CachedOperationSlotsJson;
        }

        private void FillAttackCosts(ref GridWarfareDto dto)
        {
            // L-49 FIX: Read clamped discount from PlayerAttackSystem (matches simulation formula)
            // instead of unclamped CityStabilitySystem.Discount, preventing UI/sim desync when
            // RemoteBalanceConfig.MaxDiscount exceeds GridWarfare.MaxStabilityDiscount.
            // CIVIC108: Disabled PlayerAttackSystem returns 0f discount — safe default (no discount applied)
#pragma warning disable CIVIC108 // m_StabilitySystem.Enabled guard above covers both systems
            float discount = m_StabilitySystem != null && m_StabilitySystem.Enabled && m_PlayerSystem != null
                ? m_PlayerSystem.StabilityDiscount
                : 0f;
#pragma warning restore CIVIC108

            // FIX S13-03: Include SanctionsMarkup in UI cost — matches simulation formula
            // in PlayerAttackSystem.CalculateFinalCost: baseCost * (1-discount) * (1+markup)
            float markup = 0f;
            if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
                markup = wallet.SanctionsMarkup;

            // F14: Also invalidate when stability system transitions enabled state
            bool stabilityEnabled = m_StabilitySystem != null && m_StabilitySystem.Enabled;
#pragma warning disable S1244, CIVIC072 // Intentional: exact float comparison for cache invalidation
            if (discount != m_LastDiscount || markup != m_LastSanctionsMarkup || stabilityEnabled != m_LastStabilityEnabled)
#pragma warning restore S1244, CIVIC072
            {
                m_LastDiscount = discount;
                m_LastSanctionsMarkup = markup;
                m_LastStabilityEnabled = stabilityEnabled;

                m_CachedAttackCosts = BuildAttackCosts(discount, markup);
            }

            dto.AttackCosts = m_CachedAttackCosts;
        }

        private void FillOperationEligibility(ref GridWarfareDto dto)
        {
            FillPrepareEligibility(ref dto.CanPrepareDrone, ref dto.PrepareDroneLockedReasonId, "drone");
            FillPrepareEligibility(ref dto.CanPrepareBlackout, ref dto.PrepareBlackoutLockedReasonId, "blackout");
            FillPrepareEligibility(ref dto.CanPrepareDisinfo, ref dto.PrepareDisinfoLockedReasonId, "disinfo");
        }

        private void FillPrepareEligibility(ref bool canPrepare, ref string lockedReasonId, string attackType)
        {
            canPrepare = false;
            lockedReasonId = ReasonIds.GwSystemUnavailable;

            if (m_PlayerSystem == null || !m_PlayerSystem.Enabled)
                return;

            canPrepare = m_PlayerSystem.CanPrepareOperation(attackType, out var reason);
            lockedReasonId = canPrepare ? "" : reason.ToString();
        }

        private static Dictionary<string, long> BuildAttackCosts(float discount, float markup)
        {
            var costs = new Dictionary<string, long>(AttackRegistry.Attacks.Count);
            foreach (var kvp in AttackRegistry.Attacks)
            {
                // F15: Delegate to PlayerAttackSystem.CalculateFinalCost to prevent formula divergence
                costs[kvp.Key] = PlayerAttackSystem.CalculateFinalCost(kvp.Value.BaseCost, discount, markup);
            }
            return costs;
        }

        private void FillUnlockState(ref GridWarfareDto dto)
        {
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                return;

            dto.GridWarfareUnlocked = actSingleton.CurrentAct >= Act.Adaptation;
        }

        private float GetGameTimeHours()
        {
            // FIX F7: Work in hours to avoid float precision loss at long sessions.
            return GameTimeSystem.TryGetGameHours(out var gameHours) ? gameHours : 0f;
        }

        private TriggerOutcome OnPrepareOperation(string attackType)
        {
            return ApplyOperationImmediately(attackType, OperationActionType.Prepare, $"PrepareOperation({attackType}) requested");
        }

        private TriggerOutcome OnExecuteOperation(string attackType)
        {
            return ApplyOperationImmediately(attackType, OperationActionType.Execute, $"ExecuteOperation({attackType}) requested");
        }

        private TriggerOutcome OnCancelOperation(string attackType)
        {
            return ApplyOperationImmediately(attackType, OperationActionType.Cancel, $"CancelOperation({attackType}) requested");
        }

        /// <summary>
        /// Applies GridWarfare operation commands synchronously from the UI trigger.
        /// Do not convert this back to a request entity drained by
        /// <see cref="PlayerAttackSystem.OnUpdateImpl"/>: operations such as
        /// blackout are started from pause, and the slot/wallet mutation must be
        /// visible before the click returns. Combat ECS effects are queued by
        /// PlayerAttackSystem and drained by EnemyOperationEffectSystem in
        /// ModificationEnd, so UI callbacks never mutate EnemyState directly.
        /// </summary>
        private TriggerOutcome ApplyOperationImmediately(string attackType, OperationActionType action, string acceptedLog)
        {
            if (!TryGetAttackType(attackType, out var validatedAttackType))
            {
                Log.Warn($"Rejected GridWarfare request with unknown attack type '{attackType ?? "<null>"}'");
                return TriggerOutcome.Reject(ReasonIds.GwUnknownAttack);
            }

            if (m_PlayerSystem == null || !m_PlayerSystem.Enabled)
                return TriggerOutcome.Reject(ReasonIds.GwSystemUnavailable);

            bool success;
            FixedString64Bytes failReason;
            switch (action)
            {
                case OperationActionType.Prepare:
                    success = m_PlayerSystem.PrepareOperation(validatedAttackType, out failReason);
                    break;
                case OperationActionType.Execute:
                    success = m_PlayerSystem.ExecuteOperation(validatedAttackType, out failReason);
                    break;
                case OperationActionType.Cancel:
                    success = m_PlayerSystem.CancelOperation(validatedAttackType, out failReason);
                    break;
                default:
                    Log.Error($"Unhandled OperationActionType: {action}");
                    return TriggerOutcome.Reject(ReasonIds.GwUnknownAction);
            }

            if (!success)
                return TriggerOutcome.RejectRuntime(failReason.ToString());

            Log.Info(acceptedLog);
            return TriggerOutcome.SyncSuccess(
                discriminatorKind: "operationSlot",
                discriminatorValue: $"{validatedAttackType}:{action}");
        }

        private void OnOperationPreparing(OperationPreparingEvent evt)
        {
            m_Notifications!.Push(new NarrativeToastDto(
                Channel: NotificationType.SystemAlert,
                Id: NotificationIdHelper.TimedId($"gw.op_preparing.{evt.AttackType}"),
                Title: "Operation",
                Message: $"Operation {evt.AttackType} preparing... ({evt.Duration:F0}s)",
                Status: NotificationStatus.Info));
        }

        private void OnOperationReady(OperationReadyEvent evt)
        {
            m_Notifications!.Push(new NarrativeToastDto(
                Channel: NotificationType.SystemAlert,
                Id: NotificationIdHelper.TimedId($"gw.op_ready.{evt.AttackType}"),
                Title: "Operation",
                Message: $"Operation {evt.AttackType} ready to execute!",
                Status: NotificationStatus.Success));
        }

        private void OnOperationCancelled(OperationCancelledEvent evt)
        {
            m_Notifications!.Push(new NarrativeToastDto(
                Channel: NotificationType.SystemAlert,
                Id: NotificationIdHelper.TimedId($"gw.op_cancelled.{evt.AttackType}"),
                Title: "Operation",
                Message: evt.IsConfiscated
                    ? $"Operation {evt.AttackType} seized — funds confiscated."
                    : $"Operation {evt.AttackType} cancelled. {evt.RefundedAmount} shadow refunded.",
                Status: NotificationStatus.Warning));
        }

        private static bool TryGetAttackType(string attackType, out string validatedAttackType)
        {
            validatedAttackType = string.IsNullOrWhiteSpace(attackType) ? string.Empty : attackType.Trim();
            return validatedAttackType.Length > 0 && AttackRegistry.Attacks.ContainsKey(validatedAttackType);
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<OperationPreparingEvent>(OnOperationPreparing);
            UnsubscribeSafe<OperationReadyEvent>(OnOperationReady);
            UnsubscribeSafe<OperationCancelledEvent>(OnOperationCancelled);

            base.OnDestroy();
        }
    }
}
