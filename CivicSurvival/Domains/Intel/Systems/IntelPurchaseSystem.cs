using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Domains.Intel.Systems
{
    /// <summary>
    /// Processes intel purchase requests (Data-Driven Commands pattern).
    /// UI creates IntelPurchaseRequest entity, this system processes and destroys it.
    ///
    /// Reads state from IntelStateSystem, uses ShadowEconomyEmitter + ECB for deductions.
    /// Runs after IntelStateSystem to ensure fresh state.
    /// Grants state only after retained BudgetDeductResult success.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.IntelPurchase)]
    [HandlesRequestKind(RequestKind.IntelUpgrade)]
    [TransientConsumerReconcile(typeof(IntelPurchaseRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: intel unlocks and wallet deductions are initiated only after this consumer runs, so pre-consume load loss is reissuable.")]
    public partial class IntelPurchaseSystem : CivicSystemBase
    {
        private const string REASON_INSIDER = "IntelPurchaseSystem.Insider";
        private const string REASON_UPGRADE = "IntelPurchaseSystem.Upgrade";
        // ECB command counter (encapsulated to avoid CA2211)
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private static readonly LogContext Log = new("IntelPurchaseSystem");

        private IntelStateSystem m_IntelStateSystem = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private EntityQuery m_RequestQuery;
        private EntityQuery m_PendingBudgetQuery;
        private EntityQuery m_PendingIntelUpgradeQuery;
        private EntityQuery m_CurrentActQuery;
        private ComponentLookup<IntelUpgradeBudgetIntent> m_IntelUpgradeIntentLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            m_RequestQuery = GetEntityQuery(ComponentType.ReadOnly<IntelPurchaseRequest>());
            m_PendingBudgetQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_PendingIntelUpgradeQuery = GetEntityQuery(
                ComponentType.ReadOnly<IntelUpgradeBudgetIntent>(),
                ComponentType.ReadOnly<BudgetDeductRequest>());
            m_IntelUpgradeIntentLookup = GetComponentLookup<IntelUpgradeBudgetIntent>(isReadOnly: true);
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            RequireAnyForUpdate(m_RequestQuery, m_PendingBudgetQuery);
            // CurrentActSingleton is foundational always-on (Scenario not gated). Purchase
            // validation needs the real act — never fabricate PreWar into CanBuy/CanUpgrade.
            RequireForUpdate<CurrentActSingleton>();

            Log.Info("Created (single-writer pattern)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_IntelStateSystem ??= FeatureRegistry.Instance.Require<IntelStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            using (PerformanceProfiler.Measure("IntelPurchaseSystem.OnUpdate"))
            {
                ProcessRequests();
                DrainBudgetResults();
            }
        }

        private void ProcessRequests()
        {
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            bool insiderQueuedThisFrame = false;
            bool upgradeQueuedThisFrame = false;

            // FIX H70: Reject purchases during Attack/Alert — intel becomes worthless
            // once wave transitions. WaveExecutor has no ordering vs this system.
            bool waveActive = SystemAPI.TryGetSingleton<WaveStateSingleton>(out var ws)
                && (ws.CurrentPhase == GamePhase.Attack || ws.CurrentPhase == GamePhase.Alert);

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<IntelPurchaseRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }

                string failReason = "";
                if (waveActive)
                {
                    failReason = ReasonIds.IntelWaveActive;
                    EmitResult(ecb, meta.ValueRO, request.ValueRO.PurchaseType, RequestStatus.Failed, failReason);
                    ecb.DestroyEntity(entity);
                    IncrementEcbCount();
                    continue;
                }

                bool success = request.ValueRO.PurchaseType switch
                {
                    IntelPurchaseType.Insider when insiderQueuedThisFrame || HasPendingIntelPurchase(REASON_INSIDER) => Reject(ReasonIds.IntelRequestPending, out failReason),
                    IntelPurchaseType.Insider => ProcessInsiderPurchase(request.ValueRO, meta.ValueRO, ecb, out failReason),
                    IntelPurchaseType.Upgrade when upgradeQueuedThisFrame || HasPendingIntelUpgrade() => Reject(ReasonIds.IntelRequestPending, out failReason),
                    IntelPurchaseType.Upgrade => ProcessUpgradePurchase(request.ValueRO, meta.ValueRO, ecb, out failReason),
                    _ => Reject(ReasonIds.IntelUnknownPurchaseType, out failReason)
                };
                if (success)
                {
                    if (request.ValueRO.PurchaseType == IntelPurchaseType.Insider)
                        insiderQueuedThisFrame = true;
                    else if (request.ValueRO.PurchaseType == IntelPurchaseType.Upgrade)
                        upgradeQueuedThisFrame = true;
                    else
                    {
                        // Other purchase types are one-shot and need no per-frame dedup latch.
                    }
                }

                var status = success ? RequestStatus.Pending : RequestStatus.Failed;
                Log.Info($"{request.ValueRO.PurchaseType}: {status}" + (!success ? $" - {failReason}" : ""));

                if (!success)
                    EmitResult(ecb, meta.ValueRO, request.ValueRO.PurchaseType, RequestStatus.Failed, failReason);
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }

            if (hasEcb) m_GameSimulationEndBarrier.AddJobHandleForProducer(default); // M17: system schedules no jobs; Dependency here is incoming handle, not produced work
        }

        private bool ProcessInsiderPurchase(in IntelPurchaseRequest request, in RequestMeta meta, EntityCommandBuffer ecb, out string failReason)
        {
            failReason = "";
            // Authoritative act guard. Eligibility predicates are local-facts-only now;
            // act-lock lives here (backend), in AddScenarioTrigger (click), and the
            // frontend overlay. CurrentActSingleton is a hard input (RequireForUpdate)
            // so the real act is always present — no PreWar fabrication.
            long baseCost = m_IntelStateSystem.InsiderCost;
            var currentAct = m_CurrentActQuery.GetSingleton<CurrentActSingleton>().CurrentAct;
            if (currentAct < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }
            if (!IntelEligibility.CanBuyInsider(
                    m_IntelStateSystem.HasInsider,
                    baseCost,
                    World,
                    out failReason,
                    out long cost))
            {
                if (baseCost <= 0)
                    Log.Error($"InsiderCost is {baseCost} — configuration error, blocking purchase");
                return false;
            }

            if (request.ExpectedCost != cost)
            {
                failReason = ReasonIds.InsiderPriceChanged;
                Log.Warn($"Insider purchase rejected: displayed ${request.ExpectedCost:N0}, current ${cost:N0}");
                return false;
            }

            // Retained ShadowOps deduct; state changes only after BudgetDeductResult.
            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    baseCost,
                    BudgetCategory.ShadowOps,
                    BudgetPriority.PlayerAction,
                    REASON_INSIDER,
                    meta,
                    BudgetResultMode.RetainResult))
            {
                failReason = ReasonIds.InsiderWalletUnavailable;
                return false;
            }

            Log.Info($"Insider purchased for ${cost:N0}");
            return true;
        }

        private bool ProcessUpgradePurchase(in IntelPurchaseRequest request, in RequestMeta meta, EntityCommandBuffer ecb, out string failReason)
        {
            failReason = "";
            long baseCost = m_IntelStateSystem.GetIntelUpgradeCost();
            var currentAct = m_CurrentActQuery.GetSingleton<CurrentActSingleton>().CurrentAct;
            if (currentAct < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }
            if (!IntelEligibility.CanUpgradeIntel(
                    m_IntelStateSystem.IntelUpgradeLevel >= IntelStateSystem.MAX_INTEL_UPGRADE_LEVEL,
                    baseCost,
                    World,
                    out failReason,
                    out long cost))
            {
                if (baseCost <= 0)
                    Mod.Log.Error($"[IntelPurchaseSystem] GetIntelUpgradeCost returned {baseCost} — balance misconfiguration, aborting upgrade");
                return false;
            }

            if (request.ExpectedCost != cost)
            {
                failReason = ReasonIds.IntelPriceChanged;
                Log.Warn($"Intel upgrade rejected: displayed ${request.ExpectedCost:N0}, current ${cost:N0}");
                return false;
            }

            // H-02 fix: encode POST-increment level so rollback targets the correct level.
            int newLevel = m_IntelStateSystem.IntelUpgradeLevel + 1;

            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    baseCost,
                    BudgetCategory.ShadowOps,
                    BudgetPriority.PlayerAction,
                    REASON_UPGRADE,
                    out var budgetEntity,
                    meta,
                    BudgetResultMode.RetainResult))
            {
                failReason = ReasonIds.IntelWalletUnavailable;
                return false;
            }

            ecb.AddComponent(budgetEntity, new IntelUpgradeBudgetIntent { TargetLevel = newLevel });
            Log.Info($"Intel upgrade payment queued for level {newLevel} (${cost:N0})");
            return true;
        }

        private static bool Reject(string reason, out string failReason)
        {
            failReason = reason;
            return false;
        }

        private bool HasPendingIntelPurchase(string source)
        {
            foreach (var request in SystemAPI.Query<RefRO<BudgetDeductRequest>>())
            {
                if (request.ValueRO.Source.ToString() == source)
                    return true;
            }

            return false;
        }

        private bool HasPendingIntelUpgrade() => !m_PendingIntelUpgradeQuery.IsEmpty;

        private void EmitResult(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            IntelPurchaseType purchaseType,
            RequestStatus status,
            string reasonId)
        {
            var kind = purchaseType == IntelPurchaseType.Insider
                ? RequestKind.IntelPurchase
                : RequestKind.IntelUpgrade;
            if (status == RequestStatus.Success)
                RequestResultEmitter.EmitSuccess(ecb, meta, kind, SystemAPI.Time.ElapsedTime);
            else
                RequestResultEmitter.Emit(ecb, meta, kind, status, ReasonId.FromRuntime(reasonId), SystemAPI.Time.ElapsedTime);
        }

        private void DrainBudgetResults()
        {
            if (m_PendingBudgetQuery.IsEmpty)
                return;

            m_IntelUpgradeIntentLookup.Update(this);
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            foreach (var (requestRef, resultRef, metaRef, entity) in
                SystemAPI.Query<RefRO<BudgetDeductRequest>, RefRO<BudgetDeductResult>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                string source = requestRef.ValueRO.Source.ToString();
                bool isInsider = source == REASON_INSIDER;
                bool isUpgrade = m_IntelUpgradeIntentLookup.HasComponent(entity);
                if (!isInsider && !isUpgrade)
                    continue;

                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                var result = resultRef.ValueRO;
                var purchaseType = isInsider ? IntelPurchaseType.Insider : IntelPurchaseType.Upgrade;
                if (result.Succeeded)
                {
                    int targetLevel = isUpgrade
                        ? m_IntelUpgradeIntentLookup[entity].TargetLevel
                        : 0;
                    bool applied = ApplyConfirmedPurchase(source, result.PaidAmount, targetLevel);
                    EmitResult(
                        ecb,
                        metaRef.ValueRO,
                        purchaseType,
                        applied ? RequestStatus.Success : RequestStatus.Failed,
                        applied ? string.Empty : ReasonIds.IntelPriceChanged);
                }
                else
                {
                    Log.Warn($"Intel purchase failed before state apply: {source}, amount=${result.Amount:N0}");
                    string reason = purchaseType == IntelPurchaseType.Insider
                        ? ReasonIds.InsiderWalletUnavailable
                        : ReasonIds.IntelWalletUnavailable;
                    EmitResult(ecb, metaRef.ValueRO, purchaseType, RequestStatus.Failed, reason);
                }

                ecb.DestroyEntity(entity);
            }

            if (hasEcb)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(default);
        }

        private bool ApplyConfirmedPurchase(string source, long amount, int targetLevel)
        {
            if (source == REASON_INSIDER)
            {
                if (!m_IntelStateSystem.HasInsider)
                {
                    m_IntelStateSystem.SetInsider(true);
                    m_IntelStateSystem.ForceNextUpdate();
                }
                EventBus?.SafePublish(new IntelInsiderPurchasedEvent(amount));
                Log.Info($"Insider purchase confirmed for ${amount:N0}");
                return true;
            }

            int expectedTarget = m_IntelStateSystem.IntelUpgradeLevel + 1;
            if (targetLevel != expectedTarget)
            {
                Log.Warn(
                    $"Stale Intel upgrade result ignored: target={targetLevel}, " +
                    $"current={m_IntelStateSystem.IntelUpgradeLevel}, expected={expectedTarget}");
                return false;
            }

            if (m_IntelStateSystem.IntelUpgradeLevel >= IntelStateSystem.MAX_INTEL_UPGRADE_LEVEL)
            {
                Log.Warn($"Intel upgrade result ignored at max level {m_IntelStateSystem.IntelUpgradeLevel}");
                return false;
            }

            m_IntelStateSystem.IncrementUpgradeLevel();
            m_IntelStateSystem.ForceNextUpdate();
            EventBus?.SafePublish(new IntelUpgradedEvent(m_IntelStateSystem.IntelUpgradeLevel, amount));
            Log.Info($"Intel upgrade confirmed at level {m_IntelStateSystem.IntelUpgradeLevel} for ${amount:N0}");
            return true;
        }
    }
}
