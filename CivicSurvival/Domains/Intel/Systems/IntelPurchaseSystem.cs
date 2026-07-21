using Game;
using Game.Common;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;

namespace CivicSurvival.Domains.Intel.Systems
{
    /// <summary>
    /// Processes intel purchase requests (Data-Driven Commands pattern).
    /// UI creates IntelPurchaseRequest entity, this system processes and destroys it.
    ///
    /// Pause-safe (AXIOM 14): registered in ModificationEnd and pays synchronously
    /// through IShadowWalletService (affordability preflight, then TryDeduct on the
    /// main thread). All eligibility checks run BEFORE money moves, so the former
    /// retained-deduct drain, refund compensation and per-frame dedup latches are
    /// structurally unnecessary: state is granted in the same tick as the payment.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.IntelPurchase)]
    [HandlesRequestKind(RequestKind.IntelUpgrade)]
    [TransientConsumerReconcile(typeof(IntelPurchaseRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: intel unlocks and wallet deductions happen only when this consumer runs, so pre-consume load loss is reissuable.")]
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
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private EntityQuery m_RequestQuery;
        private EntityQuery m_CurrentActQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();

            m_RequestQuery = GetEntityQuery(ComponentType.ReadOnly<IntelPurchaseRequest>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            RequireForUpdate(m_RequestQuery);
            // CurrentActSingleton is foundational always-on (Scenario not gated). Purchase
            // validation needs the real act — never fabricate PreWar into CanBuy/CanUpgrade.
            RequireForUpdate<CurrentActSingleton>();

            Log.Info("Created (synchronous wallet payment, pause-safe)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_IntelStateSystem ??= FeatureRegistry.Instance.Require<IntelStateSystem>();
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
        }

        protected override void OnUpdateImpl()
        {
            using (PerformanceProfiler.Measure("IntelPurchaseSystem.OnUpdate"))
            {
                ProcessRequests();
            }
        }

        private void ProcessRequests()
        {
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            // Purchases are allowed in every phase, including Alert/Attack/PsyAttack — the
            // War Room stays fully operable during a wave (strikes are). A mid-wave insider
            // is self-balancing: it expires at wave end (IntelStateSystem.OnWaveEnded), so
            // the player pays full price for partial coverage.
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<IntelPurchaseRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb) { ecb = m_ModificationEndBarrier.CreateCommandBuffer(); hasEcb = true; }

                string failReason;
                bool success;
                switch (request.ValueRO.PurchaseType)
                {
                    case IntelPurchaseType.Insider:
                        success = ProcessInsiderPurchase(request.ValueRO, out failReason);
                        break;
                    case IntelPurchaseType.Upgrade:
                        success = ProcessUpgradePurchase(request.ValueRO, out failReason);
                        break;
                    default:
                        success = RejectUnknown(request.ValueRO.PurchaseType, out failReason);
                        break;
                }

                var status = success ? RequestStatus.Success : RequestStatus.Failed;
                Log.Info($"{request.ValueRO.PurchaseType}: {status}" + (!success ? $" - {failReason}" : ""));

                EmitResult(ecb, meta.ValueRO, request.ValueRO.PurchaseType, status, failReason);
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }

            if (hasEcb) m_ModificationEndBarrier.AddJobHandleForProducer(default); // M17: system schedules no jobs; Dependency here is incoming handle, not produced work
        }

        private bool ProcessInsiderPurchase(in IntelPurchaseRequest request, out string failReason)
        {
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

            // Synchronous ShadowOps deduct. CanBuyInsider already resolved affordability
            // (freeze + markup + pending) and returned the effective cost.
#pragma warning disable CIVIC237 // TOCTOU safe: main-thread single writer, no yield between check and deduct
            if (!m_WalletService.TryDeduct(cost, REASON_INSIDER))
#pragma warning restore CIVIC237
            {
                failReason = ReasonIds.InsiderWalletUnavailable;
                return false;
            }

            m_IntelStateSystem.SetInsider(true);
            m_IntelStateSystem.ForceNextUpdate();
            EventBus?.SafePublish(new IntelInsiderPurchasedEvent(cost));
            Log.Info($"Insider purchased for ${cost:N0}");
            return true;
        }

        private bool ProcessUpgradePurchase(in IntelPurchaseRequest request, out string failReason)
        {
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

#pragma warning disable CIVIC237 // TOCTOU safe: main-thread single writer, no yield between check and deduct
            if (!m_WalletService.TryDeduct(cost, REASON_UPGRADE))
#pragma warning restore CIVIC237
            {
                failReason = ReasonIds.IntelWalletUnavailable;
                return false;
            }

            m_IntelStateSystem.IncrementUpgradeLevel();
            m_IntelStateSystem.ForceNextUpdate();
            EventBus?.SafePublish(new IntelUpgradedEvent(m_IntelStateSystem.IntelUpgradeLevel, cost));
            Log.Info($"Intel upgrade confirmed at level {m_IntelStateSystem.IntelUpgradeLevel} for ${cost:N0}");
            return true;
        }

        private static bool RejectUnknown(IntelPurchaseType type, out string failReason)
        {
            // Zero-init/stale request carries an unknown type — an internal defect, not
            // a player command; log it so the discard arm is never a silent drop.
            Log.Warn($"Unknown IntelPurchaseType: {type}");
            failReason = ReasonIds.IntelUnknownPurchaseType;
            return false;
        }

        private void EmitResult(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            IntelPurchaseType purchaseType,
            RequestStatus status,
            string reasonId)
        {
            RequestKind kind;
            switch (purchaseType)
            {
                case IntelPurchaseType.Insider:
                    kind = RequestKind.IntelPurchase;
                    break;
                case IntelPurchaseType.Upgrade:
                    kind = RequestKind.IntelUpgrade;
                    break;
                default:
                    // None/unknown is an internal defect (zero-init stale request), not a
                    // player command. Routing it to a real bridge key would flash a false
                    // "upgrade failed" toast — destroy-only, no result emit.
                    return;
            }
            if (status == RequestStatus.Success)
                RequestResultEmitter.EmitSuccess(ecb, meta, kind, SystemAPI.Time.ElapsedTime);
            else
                RequestResultEmitter.Emit(ecb, meta, kind, status, ReasonId.FromRuntime(reasonId), SystemAPI.Time.ElapsedTime);
        }
    }
}
