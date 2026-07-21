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
        // Monotonic-tick dedup keyed by GLOBAL HOUR (day*24+hour) — the racket pays
        // hourly slices. The struct tracks "highest handled counter" only, so the
        // day-named type fits an hour counter; the field name carries the unit.
        private DayChangedDedup m_HourDedup = default;
        private EntityQuery m_ShockQuery;
        private EntityQuery m_CurrentActQuery;
        [System.NonSerialized] private ActGateController m_Gate = null!;
        private bool m_NeedsActReconcile;
        // Global hour (day*24+hour) awaiting payout, -1 = none. Payout is deferred to
        // OnUpdateImpl because the stress cutoff must read ShockStateSingleton after
        // WorldShockSystem updated it — the tick event fires before any OnUpdateImpl.
        private int m_PendingPayoutHour = -1;
        // Carries sub-dollar slices between hours so a small rate still pays out.
        // double by contract: GameRate.AccumulateWithRemainder takes `ref double` and
        // owns the precision handling — this holds its fractional carry, never a
        // balance (CIVIC167). Not persisted: at most $1 of carry, and a load re-seeds
        // it at zero rather than paying a stale fraction (CIVIC267).
#pragma warning disable CIVIC167 // False positive: not a monetary balance but the fractional carry of GameRate.AccumulateWithRemainder, whose API is `ref double` and which owns the precision handling. Whole dollars leave here as long; drift is exactly what the carry prevents.
        [System.NonSerialized] private double m_IncomeRemainder;
#pragma warning restore CIVIC167

        // Ceiling for the fractional carry: it should never exceed a dollar or two
        // (one failed queue returns at most a single hourly slice). A cap keeps the
        // give-back path provably bounded (CIVIC226) — a runaway carry would be a
        // bug, not a balance.
        private const double MAX_INCOME_CARRY = 1_000_000.0;


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

            SubscribeRequired<HourChangedEvent>(OnHourChanged);

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
            if (m_PendingPayoutHour >= 0)
                ProcessDeferredPayout();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<HourChangedEvent>(OnHourChanged);

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
                m_HourDedup.Reset();
                m_PendingPayoutHour = -1;
                Log.Info("[VIPRacket] Gate opened");
                return;
            }

            if (next != ActGateState.Inactive)
                return;

            m_HourDedup.Reset();
            m_PendingPayoutHour = -1;
            Log.Info("[VIPRacket] Gate closed");
        }

        /// <summary>
        /// Queue one hourly slice of the racket's protection money. The daily take is
        /// flat (districts x per-VIP rate), so the slice is a plain 24th and the daily
        /// total is unchanged — a single midnight payout left the wallet unmoved for a
        /// whole game day after switching the scheme on.
        /// </summary>
        private void OnHourChanged(HourChangedEvent evt)
        {
            // EventBus dispatches regardless of the act gate.
            int globalHour = HourTick.GlobalHour(evt);
            if (m_HourDedup.IsProcessed(globalHour)) return;
            if (m_PendingPayoutHour >= 0 && m_PendingPayoutHour > m_HourDedup.LastProcessedDay)
            {
                if (m_PendingPayoutHour >= globalHour)
                    return;

                Log.Warn($"Hour {globalHour}: stale VIP payout hour {m_PendingPayoutHour} skipped before queuing latest hour");
                MarkPayoutHandled(m_PendingPayoutHour);
            }

            // Defer payout to OnUpdateImpl where ShockStateSingleton is up-to-date
            // (WorldShockSystem runs before us via RegisterAfter], but the tick event
            // fires synchronously from GameTimeSystem BEFORE any OnUpdateImpl).
            m_PendingPayoutHour = globalHour;

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
                || m_PendingPayoutHour <= m_HourDedup.LastProcessedDay))
            {
                m_PendingPayoutHour = -1;
            }

            if (Log.IsDebugEnabled) Log.Debug($"VIP first-tick reconcile: gate={m_Gate.State}, pendingHour={m_PendingPayoutHour}");
            return true;
        }

        private void ProcessDeferredPayout()
        {
            int globalHour = m_PendingPayoutHour;

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
                if (Log.IsDebugEnabled) Log.Debug($"Hour {globalHour}: VIP income skipped - assets frozen");
                MarkPayoutHandled(globalHour);
                return;
            }

            // Count VIP districts
            int vipCount = CountVipDistricts();
            if (vipCount == 0)
            {
                MarkPayoutHandled(globalHour);
                return;
            }

            // S4-04 FIX: At critical stress (50%+), oligarchs stop paying
            // Now reads ShockStateSingleton AFTER WorldShockSystem.OnUpdateImpl has updated it.
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock)
                && shock.ShockLevel >= STRESS_CUTOFF)
            {
                Log.Info($"Hour {globalHour}: Stress {shock.ShockLevel:F1}% >= {STRESS_CUTOFF}% — VIP protection failed, oligarchs stop paying");
                MarkPayoutHandled(globalHour);
                return;
            }

            // Calculate and add income via Request pattern (no cross-domain import)
            double dailyIncome = vipCount * INCOME_PER_VIP_PER_DAY;
            long hourlyIncome = GameRate.AccumulateWithRemainder(HourTick.SliceOfDaily(dailyIncome), 1.0, ref m_IncomeRemainder);
            if (hourlyIncome <= 0)
            {
                // Slice below $1 this hour — the remainder carries it to a later hour.
                MarkPayoutHandled(globalHour);
                return;
            }

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, hourlyIncome, "VIPProtection", $"VIPProtection:{globalHour}"))
            {
                m_IncomeRemainder = System.Math.Min(m_IncomeRemainder + hourlyIncome, MAX_INCOME_CARRY);
                MarkPayoutHandled(globalHour);
                return;
            }
            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            MarkPayoutHandled(globalHour);

            if (Log.IsDebugEnabled) Log.Debug($"Hour {globalHour}: {vipCount} VIP districts -> +${hourlyIncome:N0} to offshore");
        }

        private void MarkPayoutHandled(int globalHour)
        {
            m_HourDedup.MarkProcessed(globalHour);
            if (m_PendingPayoutHour == globalHour)
                m_PendingPayoutHour = -1;
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
            m_HourDedup.Reset();
            m_IncomeRemainder = 0.0; // drop the fractional carry with the rest of the state
            m_PendingPayoutHour = -1;
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
