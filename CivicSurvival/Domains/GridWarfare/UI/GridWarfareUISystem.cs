using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Game;
using Game.Common;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.Intel;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.GridWarfare;
using CivicSurvival.Core.Interfaces.Services;
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
        private IBuildingPlacementService m_PlacementCommand = null!;

        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_WalletQuery;
        private EntityQuery m_DroneLauncherQuery;

        // PERF: Cached JSON
        private string m_CachedOperationSlotsJson = JsonBuilder.EmptyArray;
        private Dictionary<string, long> m_CachedAttackCosts = BuildAttackCosts(0f, 0f);
        // m_LastSlotCount removed: #464 fix — always rebuild JSON for progress accuracy
        private float m_LastDiscount = -1f;
        private float m_LastSanctionsMarkup = -1f; // FIX S13-03
        private bool m_LastStabilityEnabled; // F14: track enabled-state transitions for cache invalidation
        private readonly OperationSlotSnapshot[] m_SlotSnapshot = new OperationSlotSnapshot[3];
        private int m_SlotObserverCursor = int.MinValue;

        // Rolling ledger of the last 8 outbound-strike outcomes (newest first), rebuilt into
        // ResolvedStrikesJson on each OutboundStrikeResolvedEvent. Main-thread only (event fires
        // in ModificationEnd, this system reads it there too), so no synchronization is needed.
        private const int ResolvedStrikeHistory = 8;

        // Lv0 intel sees enemy axis health snapped to this coarse bucket (percent) — the enemy's
        // shape without the exact targeting number.
        private const float AxisQuantizeStep = 25f;
        private readonly List<ResolvedStrikeDto> m_ResolvedStrikes = new(ResolvedStrikeHistory);
        private string m_CachedResolvedStrikesJson = JsonBuilder.EmptyArray;

        // Event-driven: phase change toasts
        private NotificationState? m_Notifications;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadOnly<EnemyState>());
            m_WalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());
            m_DroneLauncherQuery = GetEntityQuery(
                ComponentType.ReadOnly<DroneLauncherInstallation>(),
                ComponentType.Exclude<Deleted>());

            // Subscribe early, then buffer delivery until OnStartRunning resolves dependencies.
            SubscribeBufferedUntilReady<OperationPreparingEvent>(OnOperationPreparing);
            SubscribeBufferedUntilReady<OperationReadyEvent>(OnOperationReady);
            SubscribeBufferedUntilReady<OperationCancelledEvent>(OnOperationCancelled);
            SubscribeBufferedUntilReady<OutboundStrikeResolvedEvent>(OnOutboundStrikeResolved);

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
            // Shared Core placement host — activates the vanilla tool synchronously so the launcher
            // can be built while the simulation is paused (Axiom 14, AA/Center precedent).
            m_PlacementCommand ??= ServiceRegistry.Instance.Require<IBuildingPlacementService>();
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

            // Drone launcher placement — activates the vanilla placement tool synchronously (pause-safe,
            // like the Propaganda Center). Prefab is fixed (the DroneLauncher .cok); mode 0 (shadow-only).
            Triggers.Add(PlaceDroneLauncher, FeatureIds.GridWarfare, RequestResultBridge.DroneLauncherPlacement, OnPlaceDroneLauncher);

            // War Room STRIKE view target pick (Phase E). Sync, pause-safe: it only stores a NonSerialized
            // per-axis UI preference in PlayerAttackSystem (Axiom 14 route 1 — sync UI thread → managed
            // state), consulted by SelectAutoTarget at the next Execute. No ECS write, no request entity.
            Triggers.Add<int, int>(SetStrikeTarget, FeatureIds.GridWarfare, OnSetStrikeTarget);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new GridWarfareDto
            {
                CityStability = 100f,
                StabilityDiscount = 0f,
                AttackCosts = m_CachedAttackCosts,
                OperationRequestJson = RequestResultBridge.Get(RequestResultBridge.GridOperation).ToJson(),
                DroneLauncherPlacementRequestJson = RequestResultBridge.Get(RequestResultBridge.DroneLauncherPlacement).ToJson(),
                ResolvedStrikesJson = m_CachedResolvedStrikesJson,
                // NoTargetId defaults so a missing player system can never read as "target id 0 picked".
                PreferredTargetPhysical = EnemyTarget.NoTargetId,
                PreferredTargetDigital = EnemyTarget.NoTargetId,
                PreferredTargetSocial = EnemyTarget.NoTargetId
            };

            // Strike-target preference readout: the backend preference outlives the overlay, so the
            // re-opened War Room re-seeds its highlight from these instead of forgetting the pick.
            if (m_PlayerSystem != null && m_PlayerSystem.Enabled)
            {
                dto.PreferredTargetPhysical = m_PlayerSystem.GetStrikeTargetPreference(AttackCategory.Kinetic);
                dto.PreferredTargetDigital = m_PlayerSystem.GetStrikeTargetPreference(AttackCategory.Cyber);
                dto.PreferredTargetSocial = m_PlayerSystem.GetStrikeTargetPreference(AttackCategory.Psyops);
            }

            FillWallet(ref dto);
            float gameTimeHours = GetGameTimeHours();
            float gameTimeSeconds = gameTimeHours * GameRate.SECONDS_PER_HOUR;

            FillEnemy(ref dto, gameTimeHours);
            FillArsenalStock(ref dto);
            FillDroneLauncherCount(ref dto);
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

            // Intel level is the second consumer of the upgrade (Cognitive fog is the first): it gates
            // how precisely the enemy axis readout reaches the DTO. IntelStateSingleton lives in Core,
            // so reading it here does not cross a domain boundary (Axiom 5). Absent singleton reads as
            // Lv0 — the coarsest, fail-closed view. The level is the shared EFFECTIVE one (a bought
            // insider folds to L2, exactly like the mirror-city snapshot's quantization) — so the axis
            // strip, the target card and the STRIKE radar can never disagree about what the player is
            // entitled to see.
            int intelLevel = 0;
            bool fullIntel = false;
            if (SystemAPI.TryGetSingleton<IntelStateSingleton>(out var intel))
            {
                intelLevel = intel.EffectiveIntelLevel;
                fullIntel = intelLevel >= IntelStateSingleton.MaxIntelLevel;
            }
            dto.IntelLevel = intelLevel;

            // Axis precision gate: Lv0 sees only a 25%-quantized shape of the enemy — exact axis
            // health never reaches the DTO (kept sim-side, server-safe for PvP). Lv1+ sees exact.
            if (intelLevel >= 1)
            {
                dto.EnemyPhysicalAxis = state.PhysicalAxis;
                dto.EnemyDigitalAxis = state.DigitalAxis;
                dto.EnemySocialAxis = state.SocialAxis;
            }
            else
            {
                dto.EnemyPhysicalAxis = QuantizeAxis(state.PhysicalAxis);
                dto.EnemyDigitalAxis = QuantizeAxis(state.DigitalAxis);
                dto.EnemySocialAxis = QuantizeAxis(state.SocialAxis);
            }
            // Respite + act-objective readout. Active-respite flags drive the per-axis "Suppressed"
            // badge (the fact of respite is visible at every level); the countdown behind it is
            // full-intel only. ObjectiveProgress is the worst axis's normalized distance from full
            // health toward the objective threshold, so it reaches 1 only when ALL three are floored
            // enough to trigger the beachhead-collapse loot.
            var gw = BalanceConfig.Current.GridWarfare;
            dto.RespitePhysicalActive = state.IsRespiteActive(AttackCategory.Kinetic, gameTimeHours);
            dto.RespiteDigitalActive = state.IsRespiteActive(AttackCategory.Cyber, gameTimeHours);
            dto.RespiteSocialActive = state.IsRespiteActive(AttackCategory.Psyops, gameTimeHours);

            dto.RespitePhysicalHoursLeft = fullIntel ? RespiteHoursLeft(state.RespiteUntilPhysical, gameTimeHours) : -1f;
            dto.RespiteDigitalHoursLeft = fullIntel ? RespiteHoursLeft(state.RespiteUntilDigital, gameTimeHours) : -1f;
            dto.RespiteSocialHoursLeft = fullIntel ? RespiteHoursLeft(state.RespiteUntilSocial, gameTimeHours) : -1f;

            // ObjectiveProgress folds the SAME axis values the DTO ships (quantized below L1): a
            // min() over the raw axes would let the client invert the exact worst-axis health out of
            // the progress bar, bypassing the quantization gate above ("exact axis health never
            // reaches the DTO" would be false for one axis).
            float cap = gw.PressureCap;
            float threshold = gw.ObjectiveAxisThreshold;
            float denom = math.max(cap - threshold, 1f);
            float pPhysical = math.clamp((cap - dto.EnemyPhysicalAxis) / denom, 0f, 1f);
            float pDigital = math.clamp((cap - dto.EnemyDigitalAxis) / denom, 0f, 1f);
            float pSocial = math.clamp((cap - dto.EnemySocialAxis) / denom, 0f, 1f);
            dto.ObjectiveProgress = math.min(pPhysical, math.min(pDigital, pSocial));

            // Aggregate enemy health (mean axis / cap) — the wave-scaling seam; the War Room strip
            // reads it as both the AGGREGATE readout and the NEXT WAVE SCALE multiplier. Full-intel
            // only: the -1 sentinel hides both rows below max intel.
            dto.EnemyAggregatePressure01 = fullIntel ? state.AggregatePressure01(cap) : -1f;
        }

        // Lv0 sees enemy axis health rounded to the nearest 25% — the shape of the enemy without the
        // exact targeting number. Kept static (no per-axis state) and sim-side of the DTO seam.
        private static float QuantizeAxis(float axis) => math.round(axis / AxisQuantizeStep) * AxisQuantizeStep;

        // Game-hours remaining on an axis respite, or -1 when it has lapsed / never armed.
        private static float RespiteHoursLeft(float respiteUntilHours, float nowHours)
            => respiteUntilHours > 0f && nowHours < respiteUntilHours ? respiteUntilHours - nowHours : -1f;

        private void FillArsenalStock(ref GridWarfareDto dto)
        {
            // Null-object service reports IsAvailable=false and StockOf=0 — readout
            // shows zero stock until the arsenal singleton is live (fail-closed).
            if (!m_Arsenal.IsAvailable) return;

            dto.DroneStock = m_Arsenal.StockOf(ArsenalKind.Drone);
            dto.BallisticStock = m_Arsenal.StockOf(ArsenalKind.Ballistic);
        }

        private void FillDroneLauncherCount(ref GridWarfareDto dto)
        {
            // Cheap structural count of live launcher installs — gates the drone EXECUTE button
            // (0 = outbound drones have no launch origin). CalculateEntityCount is a metadata read,
            // no chunk scan / Transform touch.
            dto.DroneLauncherCount = m_DroneLauncherQuery.CalculateEntityCount();
            // Price the placement handler will charge (pre-markup shadow) — the STRIKE build card
            // names the purse instead of leaving the cost to be discovered at placement time.
            dto.DroneLauncherCost = BalanceConfig.Current.GridWarfare.DroneLauncherCost;
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

            dto.GridWarfareUnlocked = actSingleton.CurrentAct >= Act.Crisis;
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
            // Every outcome — success AND rejection — must carry the same
            // "operationSlot" discriminator: the operation cards scope their
            // pending state by it, and a bare rejection leaves the clicked card
            // on "Processing…" forever while its single-flight guard swallows
            // every other operation button.
            if (!TryGetAttackType(attackType, out var validatedAttackType))
            {
                Log.Warn($"Rejected GridWarfare request with unknown attack type '{attackType ?? "<null>"}'");
                return TriggerOutcome.Reject(
                    ReasonIds.GwUnknownAttack,
                    discriminatorKind: "operationSlot",
                    discriminatorValue: $"{attackType}:{action}");
            }

            if (m_PlayerSystem == null || !m_PlayerSystem.Enabled)
                return TriggerOutcome.Reject(
                    ReasonIds.GwSystemUnavailable,
                    discriminatorKind: "operationSlot",
                    discriminatorValue: $"{validatedAttackType}:{action}");

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
                    return TriggerOutcome.Reject(
                        ReasonIds.GwUnknownAction,
                        discriminatorKind: "operationSlot",
                        discriminatorValue: $"{validatedAttackType}:{action}");
            }

            if (!success)
                return TriggerOutcome.RejectRuntime(
                    failReason.ToString(),
                    discriminatorKind: "operationSlot",
                    discriminatorValue: $"{validatedAttackType}:{action}");

            Log.Info(acceptedLog);
            return TriggerOutcome.SyncSuccess(
                discriminatorKind: "operationSlot",
                discriminatorValue: $"{validatedAttackType}:{action}");
        }

        // Drone launcher placement — activates the vanilla placement tool synchronously so building
        // works while the simulation is paused (Axiom 14, AA/Center precedent). Prefab is fixed
        // (the DroneLauncher .cok); mode 0 (shadow-only). Supersede so a re-click cancels the prior
        // pending tool.
        private TriggerOutcome OnPlaceDroneLauncher()
        {
            Log.Info("OnPlaceDroneLauncher called");
            return TriggerOutcome.Supersede(ReasonIds.DroneLauncherCancelled, token =>
                ActivateDroneLauncherPlacementImmediately(token));
        }

        private void ActivateDroneLauncherPlacementImmediately(RequestToken token)
        {
            var result = m_PlacementCommand.TryActivatePlacement(
                BuildingPlacementKind.DroneLauncher,
                "DroneLauncher",
                (byte)0,
                token);

            if (!result.Activated)
                RequestResultBridge.Complete(token, RequestStatus.Failed, result.ReasonId.ToString());
        }

        /// <summary>
        /// War Room STRIKE view target pick (Phase E). Stores the player's per-axis preferred mirror-city
        /// target synchronously so the next Execute of that axis's operation aims at it (if still valid).
        /// <paramref name="axisRaw"/> is an <see cref="AttackCategory"/> (0 physical / 1 digital / 2 social);
        /// <paramref name="targetId"/> is an <see cref="EnemyTarget.Id"/>, or 0xFFFF (NoTargetId) to clear
        /// back to auto-select. Pure managed-state write on the UI thread — pause-safe (Axiom 14 route 1),
        /// no ECS mutation and no request entity.
        /// </summary>
        private void OnSetStrikeTarget(int axisRaw, int targetId)
        {
            if (axisRaw < 0 || axisRaw > 2)
            {
                Log.Warn($"SetStrikeTarget rejected: unknown axis {axisRaw}");
                return;
            }
            if (m_PlayerSystem == null || !m_PlayerSystem.Enabled)
                return;

            // Clamp to the ushort id space; anything outside it (or the explicit NoTargetId sentinel) clears
            // the preference back to auto-select.
            ushort id = targetId >= 0 && targetId < EnemyTarget.NoTargetId
                ? (ushort)targetId
                : EnemyTarget.NoTargetId;

            m_PlayerSystem.SetStrikeTargetPreference((AttackCategory)axisRaw, id);
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

        private void OnOutboundStrikeResolved(OutboundStrikeResolvedEvent evt)
        {
            string axis = AxisName(evt.Axis);

            // Newest first; drop the oldest once the ledger passes its cap.
            m_ResolvedStrikes.Insert(0, new ResolvedStrikeDto
            {
                Axis = axis,
                Intercepted = evt.Intercepted,
                NoEffect = evt.NoEffect,
                OldValue = evt.OldValue,
                NewValue = evt.NewValue,
                Seed = evt.Seed,
                TargetId = evt.TargetId
            });
            if (m_ResolvedStrikes.Count > ResolvedStrikeHistory)
                m_ResolvedStrikes.RemoveRange(ResolvedStrikeHistory, m_ResolvedStrikes.Count - ResolvedStrikeHistory);

            RebuildResolvedStrikesJson();

            // Toast on every outcome so the player sees strike results with the War Room closed.
            if (evt.Intercepted)
            {
                m_Notifications!.Push(new NarrativeToastDto(
                    Channel: NotificationType.SystemAlert,
                    Id: NotificationIdHelper.TimedId($"gw.strike_intercepted.{axis}"),
                    Title: "Counter-Strike",
                    Message: $"Outbound {axis} strike intercepted over enemy lines.",
                    Status: NotificationStatus.Warning));
            }
            else if (evt.NoEffect)
            {
                // Landed on the invulnerable reserve — a guaranteed zero. A success toast reading
                // "-0%" would misreport this as a hit; say plainly that the axis has no effective
                // target left.
                m_Notifications!.Push(new NarrativeToastDto(
                    Channel: NotificationType.SystemAlert,
                    Id: NotificationIdHelper.TimedId($"gw.strike_noeffect.{axis}"),
                    Title: "Counter-Strike",
                    Message: $"Strike landed, but enemy {axis} is stripped to its hardened reserve — no effective target.",
                    Status: NotificationStatus.Warning));
            }
            else
            {
                float delta = evt.OldValue - evt.NewValue;
                m_Notifications!.Push(new NarrativeToastDto(
                    Channel: NotificationType.SystemAlert,
                    Id: NotificationIdHelper.TimedId($"gw.strike_hit.{axis}"),
                    Title: "Counter-Strike",
                    Message: $"Strike landed — enemy {axis} -{delta:F0}%.",
                    Status: NotificationStatus.Success));
            }
        }

        private void RebuildResolvedStrikesJson()
        {
            if (m_ResolvedStrikes.Count == 0)
            {
                m_CachedResolvedStrikesJson = JsonBuilder.EmptyArray;
                return;
            }

            var sb = new StringBuilder(256);
            sb.Append('[');
            for (int i = 0; i < m_ResolvedStrikes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                m_ResolvedStrikes[i].WriteTo(sb);
            }
            sb.Append(']');
            m_CachedResolvedStrikesJson = sb.ToString();
        }

        private static string AxisName(AttackCategory axis) => axis switch
        {
            AttackCategory.Kinetic => "physical",
            AttackCategory.Cyber => "digital",
            AttackCategory.Psyops => "social",
            _ => throw new System.ArgumentOutOfRangeException(nameof(axis), axis, "Unknown AttackCategory — add case to GridWarfareUISystem.AxisName")
        };

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
            UnsubscribeSafe<OutboundStrikeResolvedEvent>(OnOutboundStrikeResolved);

            base.OnDestroy();
        }
    }
}
