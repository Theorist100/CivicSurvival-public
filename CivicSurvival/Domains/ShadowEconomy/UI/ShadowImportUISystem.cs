using System;
using CivicSurvival.Core.UI;
using Unity.Entities;
using Unity.Mathematics;
using Game;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Services.Economy;
using CivicSurvival.Core.Systems;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ShadowEconomy.UI
{
    /// <summary>
    /// UI system for shadow import data (black market electricity).
    /// ECS-Pure: Reads from ECS singletons, creates ShadowTradeRequest entities.
    ///
    /// Migrated from ShadowImportUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class ShadowImportUISystem : CivicUIPanelSystem
    {
        private const float DEFAULT_SHADOW_IMPORT_PRICE = 600f;
        private const int ImportPresetPayloadOffset = 1;
        private const int MaxImportPresetPercent = 100;
        private static readonly int[] s_ImportPresetPercents = { 0, 25, 50, 75, 100 };


        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_ShadowTradeQuery;
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_CountermeasuresQuery;
        private EntityQuery m_WalletQuery;

        private ModSettings? m_Settings;
        private IShadowTradeConsumerReadiness m_Readiness = null!;
        private ICounterAttackArsenalService m_Arsenal = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            m_ShadowTradeQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowImportState>());
            m_PowerGridQuery = GetEntityQuery(
                ComponentType.ReadOnly<PowerGridSingleton>());
            m_CountermeasuresQuery = GetEntityQuery(
                ComponentType.ReadOnly<CountermeasuresCoreFsm>());
            m_WalletQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowWalletSingleton>());

            RequireForUpdate(m_ShadowTradeQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            m_Readiness = ServiceRegistry.Instance.Require<IShadowTradeConsumerReadiness>();
            // Cross-feature service lookup after FeatureRegistry boot (CIVIC403); null-object
            // when GridWarfare is closed so AllocateProcurementBatchId()=0 → emitter drops the batch.
            m_Arsenal = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCounterAttackArsenalService.Instance);
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(ImportState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(
                SetShadowImportMW,
                FeatureIds.ShadowEconomy,
                RequestResultBridge.ShadowTradeImport,
                ActionKey.ShadowImport,
                BuildImportActionContext,
                OnSetImportMW);

            // Counter-attack arsenal purchase via shadow import (channel a). Gated by the
            // GridWarfare feature — the arsenal it feeds belongs to GridWarfare, not the
            // shadow market. Payload: (kindRaw, count). Fire-and-forget on the UI thread
            // (no request-result bridge): the procurement batch + budget intent are queued
            // on EndFrameBarrier (pause-safe) and CounterAttackArsenalSystem grants/rejects
            // the stock, publishing ArsenalProcurementEvent for UI feedback.
            Triggers.Add<int, int>(
                PurchaseCounterAttackArsenal,
                FeatureIds.GridWarfare,
                OnPurchaseArsenal);
        }

        protected override void OnPanelUpdate()
        {
            if (!m_ShadowTradeQuery.TryGetSingleton<ShadowImportState>(out var state))
                return;

            // FIX S3-08: Pass wallet frozen state to import UI
            bool walletFrozen = false;
            int freezeReason = 0;
            float sanctionsMarkup = 0f;
            if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var walletForFreeze))
            {
                walletFrozen = walletForFreeze.IsFrozen;
                freezeReason = (int)walletForFreeze.FreezeReason;
                sanctionsMarkup = walletForFreeze.SanctionsMarkup;
            }

            int maxImportMW = CalculateMaxImport();
            var actionGate = ActionGate.Resolve(ActionKey.ShadowImport, BuildShadowTradeContext(1));
            ActionAvailabilityField importAvailability;
            if (!actionGate.CanRun)
            {
                importAvailability = actionGate;
            }
            else
            {
                importAvailability = ShadowImportEligibility.ShadowImportAvailable(
                    state.ImportIsSanctioned,
                    walletFrozen,
                    out var importLockedReasonId)
                    ? ActionAvailabilityField.Allow()
                    : ActionAvailabilityField.Reject(importLockedReasonId);
            }
            var dto = new ImportDto
            {
                ShadowImportMW = state.ImportMW,
                MaxShadowImportMW = maxImportMW,
                SelectedPresetIndex = CalculateSelectedPresetIndex(state.ImportMW, maxImportMW),
                ShadowImportCost = CalculateDailyCost(state.ImportMW, sanctionsMarkup),
                DiscoveryRisk = state.ImportDiscoveryRisk,
                ShadowImportDaysActive = state.ImportDaysActive,
                IsSanctioned = state.ImportIsSanctioned,
                ShadowImportSanctionDays = state.ImportSanctionDaysRemaining,
                ShadowImportAvailability = importAvailability,
                IsFrozen = walletFrozen,
                FreezeReason = freezeReason,
                ShadowTradeImportRequestJson = RequestResultBridge.Get(RequestResultBridge.ShadowTradeImport).ToJson()
            };

            PublishWhenComplete(ImportState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnSetImportMW(int payload)
        {
            if (!TryRequireShadowTradeConsumerReady(out var lockedReasonId))
                return TriggerOutcome.Reject(lockedReasonId);

            if (TriggerOutcome.IsSimulationPaused(World))
                return TriggerOutcome.Reject(ReasonIds.GamePaused);

            bool isPresetPayload = TryDecodeImportPresetPayload(payload, out int presetPercent);
            if (payload < 0 && !isPresetPayload)
            {
                return TriggerOutcome.Reject(ReasonIds.MarketInvalidInput);
            }
            bool hasImportState = m_ShadowTradeQuery.TryGetSingleton<ShadowImportState>(out _);
            if (!hasImportState)
            {
                if (Log.IsDebugEnabled) Log.Debug($"SetShadowImportMW({payload}) rejected: shadow trade state missing");
                return TriggerOutcome.Reject(ReasonIds.MarketStateUnavailable);
            }

            int maxMW = CalculateMaxImport();
            int requestedMW = isPresetPayload
                ? ShadowImportCalculator.CalculateImportMWForPercent(maxMW, presetPercent)
                : math.clamp(payload, 0, maxMW);

            // Block non-zero requests when wallet frozen; allow mw=0 cancel through
            float sanctionsMarkup = 0f;
            if (!m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
            {
                if (Log.IsDebugEnabled) Log.Debug($"SetShadowImportMW({payload}) rejected: wallet missing");
                return TriggerOutcome.Reject(ReasonIds.MarketWalletUnavailable);
            }

            sanctionsMarkup = wallet.SanctionsMarkup;
            long expectedDailyCost = CalculateDailyCostLong(requestedMW, sanctionsMarkup);
            if (requestedMW > 0 && !ShadowImportEligibility.CanSetImportMW(
                    importStateAvailable: hasImportState,
                    walletAvailable: true,
                    walletFrozen: wallet.IsFrozen,
                    walletBalance: wallet.Balance,
                    effectiveCost: expectedDailyCost,
                    out var reasonId))
            {
                Log.Info($"Cannot set shadow import: {reasonId} (need ${expectedDailyCost:N0})");
                return TriggerOutcome.RejectRuntime(reasonId);
            }

            if (!isPresetPayload && payload != requestedMW && Log.IsDebugEnabled)
                Log.Debug($"SetImportMW clamped in UI: requested={payload}, accepted={requestedMW}, max={maxMW}");

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            return TriggerOutcome.Pending(token =>
            {
                if (!GameTimeSystem.TryGetTotalGameSeconds(out var nowSeconds))
                    return false;

                bool queued = ShadowEconomyEmitter.TryQueueSetImportMW(
                    ecb,
                    requestedMW,
                    maxMW,
                    expectedDailyCost,
                    (float)nowSeconds,
                    token,
                    isPresetPayload ? presetPercent : ShadowTradeRequest.NoPresetPercent);
                if (queued && Log.IsDebugEnabled)
                    Log.Debug(isPresetPayload
                        ? $"Created ShadowTradeRequest: SetImportMW({presetPercent}% => {requestedMW} MW)"
                        : $"Created ShadowTradeRequest: SetImportMW({requestedMW})");
                return queued;
            }, ReasonIds.MarketRequestFailed);
        }

        /// <summary>
        /// Channel (a): buy counter-attack munitions on the shadow market. Runs on the UI
        /// thread; queues a paid procurement batch on EndFrameBarrier (pause-safe) routed
        /// through BudgetCategory.ShadowOps so SanctionsMarkup + the shadow-wallet pending
        /// reservation are applied inside BudgetEmitter — the wallet is never touched here.
        /// payloadKind: 0 = drone, 1 = ballistic. count: units to buy (clamped 1..max).
        /// </summary>
        private void OnPurchaseArsenal(int payloadKind, int count)
        {
            if (count <= 0 || payloadKind < 0 || payloadKind > (int)ArsenalKind.Ballistic)
            {
                if (Log.IsDebugEnabled) Log.Debug($"PurchaseArsenal rejected: bad payload kind={payloadKind} count={count}");
                return;
            }

            // Offensive arsenal follows the launch gate (Act.Adaptation), not the shadow
            // import readiness gate — you stockpile counter-attack munitions once the
            // offensive is unlockable, mirroring PlayerAttackSystem's act-lock.
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                || actSingleton.CurrentAct < Act.Adaptation)
            {
                if (Log.IsDebugEnabled) Log.Debug("PurchaseArsenal rejected: pre-Adaptation act");
                return;
            }

            // Reject in pause, symmetric with OnSetImportMW. The procurement batch drains
            // in CounterAttackArsenalSystem (GameSimulation), which does not tick while
            // paused (Axiom 14) — without this gate the queued batch (and its budget
            // reservation) would sit silent until unpause, so the click looks dead.
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("PurchaseArsenal rejected: simulation paused");
                return;
            }

            // Arsenal singleton must be live before we allocate a batch id (fail-closed):
            // a null-object service returns 0, which the emitter rejects.
            if (!m_Arsenal.IsAvailable)
            {
                Log.Info("PurchaseArsenal rejected: arsenal service unavailable");
                return;
            }

            var kind = (ArsenalKind)payloadKind;
            int clampedCount = math.clamp(count, 1, BalanceConfig.Current.GridWarfare.ArsenalMaxPurchaseCount);
            long baseCost = ArsenalBaseCost(kind) * clampedCount;

            // Early affordability check (mirror of the emitter's internal precheck) so we
            // don't allocate a batch id we know will fail at the shadow wallet.
            if (!ArsenalProcurementEmitter.CanAffordProcurement(World, baseCost, BudgetCategory.ShadowOps))
            {
                Log.Info($"PurchaseArsenal rejected: shadow wallet can't cover ${baseCost:N0} for {clampedCount}x {kind}");
                return;
            }

            long batchId = m_Arsenal.AllocateProcurementBatchId();
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            bool queued = ArsenalProcurementEmitter.QueuePaidProcurement(
                World,
                ecb,
                batchId,
                kind,
                clampedCount,
                baseCost,
                BudgetCategory.ShadowOps,
                "ShadowImport");

            if (queued)
                Log.Info($"Queued shadow-import arsenal procurement: {clampedCount}x {kind} batch={batchId} base=${baseCost:N0}");
            else if (Log.IsDebugEnabled)
                Log.Debug($"PurchaseArsenal batch {batchId} not queued ({clampedCount}x {kind})");
        }

        private static long ArsenalBaseCost(ArsenalKind kind)
        {
            var gw = BalanceConfig.Current.GridWarfare;
            return kind == ArsenalKind.Ballistic ? gw.ArsenalBallisticBaseCost : gw.ArsenalDroneBaseCost;
        }

        private int CalculateMaxImport()
        {
            int consumption = 0;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
            {
                consumption = grid.Consumption / 1000;
            }

            float corruptionScore = 0f;
            if (m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var cmCore))
            {
                corruptionScore = cmCore.CorruptionScore;
            }

            return ShadowImportCalculator.CalculateMaxImportMW(consumption, corruptionScore);
        }

        private bool TryRequireShadowTradeConsumerReady(out ReasonId lockedReasonId)
        {
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
            {
                lockedReasonId = ReasonIds.ScenarioUnavailable;
                return false;
            }

            if (actSingleton.CurrentAct < Act.Crisis)
            {
                lockedReasonId = ReasonIds.PreWarLocked;
                return false;
            }

            if (!m_Readiness.CanConsumeShadowTradeRequests)
            {
                lockedReasonId = ReasonIds.MarketStateUnavailable;
                return false;
            }

            lockedReasonId = ReasonId.None;
            return true;
        }

        private static int CalculateSelectedPresetIndex(int importMW, int maxImportMW)
        {
            int clampedImport = math.clamp(importMW, 0, math.max(0, maxImportMW));
            if (maxImportMW <= 0)
                return clampedImport == 0 ? 0 : -1;

            int effectivePercent = (int)math.round(clampedImport * 100f / maxImportMW);
            int bestIndex = -1;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < s_ImportPresetPercents.Length; i++)
            {
                int distance = Math.Abs(effectivePercent - s_ImportPresetPercents[i]);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestIndex = i;
            }

            return bestIndex;
        }

        private static bool TryDecodeImportPresetPayload(int payload, out int presetPercent)
        {
            presetPercent = 0;
            if (payload >= 0)
                return false;

            int decoded = -payload - ImportPresetPayloadOffset;
            if (decoded < 0 || decoded > MaxImportPresetPercent)
                return false;

            presetPercent = decoded;
            return true;
        }

        private int CalculateDailyCost(int importMW, float sanctionsMarkup)
        {
            long cost = CalculateDailyCostLong(importMW, sanctionsMarkup);
            if (cost >= int.MaxValue)
                return int.MaxValue;
            return checked((int)cost);
        }

        private long CalculateDailyCostLong(int importMW, float sanctionsMarkup)
        {
            float price = m_Settings?.ShadowImportPrice ?? DEFAULT_SHADOW_IMPORT_PRICE;
            long baseCost = ShadowImportCalculator.CalculateDailyCostLong(importMW, price);
            return SanctionsCostHelper.ApplyMarkup(baseCost, sanctionsMarkup);
        }

        private ActionContext BuildImportActionContext(int mw)
        {
            if (mw <= 0)
                return BuildShadowTradeContext(0);

            int maxMW = CalculateMaxImport();
            int requestedMW = math.clamp(mw, 0, maxMW);
            float price = m_Settings?.ShadowImportPrice ?? DEFAULT_SHADOW_IMPORT_PRICE;
            long baseCost = ShadowImportCalculator.CalculateDailyCostLong(requestedMW, price);
            return BuildShadowTradeContext(baseCost);
        }

        private ActionContext BuildShadowTradeContext(long proposedCost)
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                false,
                GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar).WithCost(proposedCost);

            return m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)
                ? ctx.WithWallet(wallet)
                : ctx;
        }

    }
}
