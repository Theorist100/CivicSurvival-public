using Game;
using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;
using Game.Simulation;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// VIP Protection Racket: Oligarchs pay for grid protection.
    ///
    /// Mechanics:
    /// - Each VIP district pays daily Shadow Cash
    /// - Income = VIP_COUNT * RATE_PER_DAY
    /// - At CRITICAL stress (50%+), VIP protection fails — oligarchs stop paying
    /// - This is "protection money" — oligarchs pay mayor to keep their lights on
    ///
    /// BUG-PL-020 FIX: Creates economic pressure against "all VIP" exploit.
    /// </summary>
#pragma warning disable S101 // VIP is a standard acronym, intentionally uppercase
    public partial class VIPProtectionRacketSystem : CivicSystemBase, IDefaultSerializable, IResettable, IActGatedSystem
#pragma warning restore S101
    {
        private static readonly LogContext Log = new("VIPRacket");

        // Config: $2000/day per VIP district (from BalanceConfig or hardcoded)
        private const int INCOME_PER_VIP_PER_DAY = 2000;
        // S4-04 FIX: At critical stress, oligarchs stop paying (documented in class docstring)
        private const float STRESS_CUTOFF = 50f;

        private IDistrictStateReader m_DistrictState = null!;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private DayChangedDedup m_DayDedup = default;
        private EntityQuery m_ShockQuery;
        private EntityQuery m_CurrentActQuery;
        [System.NonSerialized] private ActGateController m_Gate = null!;
        private bool m_NeedsActReconcile;
        private int m_PendingPayoutDay = -1;

        public Act MinActiveAct => Act.Exodus;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Services wired in OnStartRunning — ServiceRegistry registration is
            // still in progress while OnCreate runs.
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_ShockQuery = GetEntityQuery(ComponentType.ReadOnly<ShockStateSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_Gate = CreateGate();

            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.Income);

            Log.Info($"{nameof(VIPProtectionRacketSystem)} created (gate awaits act state)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // FeatureRegistry registration is complete by the time OnStartRunning fires.
            ResolveServices();
        }

        protected override void OnUpdateImpl()
        {
            ResolveServices();
            if (!RefreshGate(clearStalePending: true))
                return;

            if (m_Gate.State != ActGateState.Active)
                return;

            // Deferred payout: shock singleton now up-to-date (WorldShockSystem runs before us)
            if (m_PendingPayoutDay >= 0)
                ProcessDeferredPayout();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);

            Log.Info($"{nameof(VIPProtectionRacketSystem)} destroyed");
            base.OnDestroy();
        }

        private ActGateController CreateGate()
            => new(
                isOpenFor: IsVipProtectionAct,
                onTransition: HandleGateTransition);

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (isInitial)
                return;

            if (next == ActGateState.Active)
            {
                m_DayDedup.Reset();
                m_PendingPayoutDay = -1;
                Log.Info("[VIPRacket] Gate opened");
                return;
            }

            if (next != ActGateState.Inactive)
                return;

            m_DayDedup.Reset();
            m_PendingPayoutDay = -1;
            Log.Info("[VIPRacket] Gate closed");
        }

        private void OnDayChanged(DayChangedEvent evt)
        {
            // EventBus dispatches regardless of the act gate.
            if (m_DayDedup.IsProcessed(evt.DayNumber)) return;
            if (m_PendingPayoutDay >= 0 && m_PendingPayoutDay > m_DayDedup.LastProcessedDay)
            {
                if (m_PendingPayoutDay >= evt.DayNumber)
                    return;

                Log.Warn($"Day {evt.DayNumber}: stale VIP payout day {m_PendingPayoutDay} skipped before queuing latest day");
                MarkPayoutHandled(m_PendingPayoutDay);
            }

            // Defer payout to OnUpdateImpl where ShockStateSingleton is up-to-date
            // (WorldShockSystem runs before us via RegisterAfter], but DayChanged fires
            // synchronously from GameTimeSystem BEFORE any OnUpdateImpl).
            m_PendingPayoutDay = evt.DayNumber;

            RefreshGate(clearStalePending: false);
        }

        private bool RefreshGate(bool clearStalePending)
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);

            // One-time reconciliation after load/reset; CurrentActSingleton may not be ready at Deserialize time.
            if (!m_NeedsActReconcile)
                return true;

            if (m_Gate.State == ActGateState.AwaitingActState)
                return false;

            m_NeedsActReconcile = false;
            if (clearStalePending
                && (m_Gate.State != ActGateState.Active
                || m_PendingPayoutDay <= m_DayDedup.LastProcessedDay))
            {
                m_PendingPayoutDay = -1;
            }

            if (Log.IsDebugEnabled) Log.Debug($"VIP first-tick reconcile: gate={m_Gate.State}, pendingDay={m_PendingPayoutDay}");
            return true;
        }

        private void ProcessDeferredPayout()
        {
            int dayNumber = m_PendingPayoutDay;

            using var _ = PerformanceProfiler.Measure("VIPProtection.Payout");
            ResolveServices();

            // Guard: wallet must be operational (mirrors FuelSiphoningSystem/EmergencyFundSystem pattern).
            // m_WalletService is wired by OnStartRunning; default null-object's IsOperational=false
            // makes the early-return safe even before the first OnStartRunning tick.
            if (!m_WalletService.IsOperational)
                return;
            // M01 fix: freeze blocks ALL shadow economy activity (income + spending)
            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {dayNumber}: VIP income skipped - assets frozen");
                MarkPayoutHandled(dayNumber);
                return;
            }

            // Count VIP districts
            int vipCount = CountVipDistricts();
            if (vipCount == 0)
            {
                MarkPayoutHandled(dayNumber);
                return;
            }

            // S4-04 FIX: At critical stress (50%+), oligarchs stop paying
            // Now reads ShockStateSingleton AFTER WorldShockSystem.OnUpdateImpl has updated it.
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock)
                && shock.ShockLevel >= STRESS_CUTOFF)
            {
                Log.Info($"Day {dayNumber}: Stress {shock.ShockLevel:F1}% >= {STRESS_CUTOFF}% — VIP protection failed, oligarchs stop paying");
                MarkPayoutHandled(dayNumber);
                return;
            }

            // Calculate and add income via Request pattern (no cross-domain import)
            int dailyIncome = vipCount * INCOME_PER_VIP_PER_DAY;
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, dailyIncome, "VIPProtection", $"VIPProtection:{dayNumber}"))
            {
                MarkPayoutHandled(dayNumber);
                return;
            }
            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            MarkPayoutHandled(dayNumber);

            Log.Info($"Day {dayNumber}: {vipCount} VIP districts -> +${dailyIncome:N0} to offshore");
        }

        private void MarkPayoutHandled(int dayNumber)
        {
            m_DayDedup.MarkProcessed(dayNumber);
            if (m_PendingPayoutDay == dayNumber)
                m_PendingPayoutDay = -1;
        }

        private int CountVipDistricts()
        {
            ResolveServices();
            var districtState = m_DistrictState;
            if (districtState == null)
                return 0;
            var snapshot = districtState.TakeSnapshot();
            return snapshot.VipCount;
        }

        private static bool IsVipProtectionAct(Act act)
            => act is Act.Exodus or Act.Adaptation or Act.Routine;

        /// <summary>CIVIC085 FIX: Reset state for new game.</summary>
        public void ResetState()
        {
            m_DayDedup.Reset();
            m_PendingPayoutDay = -1;
            m_WalletService = NullShadowWalletService.Instance;
            m_Gate = CreateGate();
            m_NeedsActReconcile = true;
        }

        private void ResolveServices()
        {
            m_DistrictState ??= ServiceRegistry.Instance.Require<IDistrictStateReader>();
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
        }
    }
}
