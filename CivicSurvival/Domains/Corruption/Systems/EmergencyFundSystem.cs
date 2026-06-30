using System;
using Game;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Emergency Fund Raid: Withdraw city emergency reserves for personal gain.
    ///
    /// Mechanics:
    /// - Player sets withdraw percentage via CorruptionSchemeRequest (0%, 25%, 50%, 75%, 100%)
    /// - State stored in EmergencyFundSingleton
    /// - Withdrawn amount goes to offshore account (handled by OnDayChanged)
    /// - Without fund: disaster penalties doubled
    ///
    /// Single-writer pattern: Uses ShadowIncomeRequest for wallet writes.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(EmergencyFundSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class EmergencyFundSystem : CivicSystemBase, ICivicSingletonOwner<EmergencyFundSingleton>
    {
        private static readonly LogContext Log = new("EmergencyFund");

        // ============================================================================
        // ECS STATE
        // ============================================================================

        private EntityQuery m_SingletonQuery;
        private EntityQuery m_ConfigQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private IShadowWalletService m_WalletService = null!;
        private DayChangedDedup m_DayDedup = default;
        // S12a-6 FIX: ComponentLookup instead of EntityManager in event callback
        private ComponentLookup<EmergencyFundSingleton> m_EmergencyFundLookup;
        [System.NonSerialized] private bool m_SingletonMissingWarned;
        [System.NonSerialized] private bool m_ConfigMissingWarned;
        [System.NonSerialized] private EmergencyFundSingleton m_LiveEmergencyFund;
        [System.NonSerialized] private EmergencyFundSettings m_LiveEmergencyFundSettings;

        // ============================================================================
        // PROPERTIES (read from singleton)
        // ============================================================================

        private EmergencyFundSingleton ReadSingleton()
        {
            if (m_HasRestoredEmergencyFund)
                return m_LiveEmergencyFund;

            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_SingletonQuery.TryGetSingleton<EmergencyFundSingleton>(out var singleton))
            {
                if (!m_SingletonMissingWarned)
                {
                    m_SingletonMissingWarned = true;
                    Log.Warn("EmergencyFundSingleton missing — returning Default. Singleton entity may have been destroyed.");
                }
                return m_LiveEmergencyFund;
            }
            m_LiveEmergencyFund = singleton;
            return singleton;
        }

        private EmergencyFundSettings ReadConfig()
        {
            if (m_HasRestoredEmergencyFund)
                return m_LiveEmergencyFundSettings;

            if (!m_ConfigQuery.TryGetSingleton<EmergencyFundSettings>(out var config))
            {
                if (!m_ConfigMissingWarned)
                {
                    m_ConfigMissingWarned = true;
                    Log.Warn("EmergencyFundSettings missing — returning Default. Config singleton entity may have been destroyed.");
                }
                return m_LiveEmergencyFundSettings;
            }
            m_LiveEmergencyFundSettings = config;
            return config;
        }

        /// <summary>Initial emergency fund balance.</summary>
        public double InitialBalance => ReadSingleton().InitialBalance;

        /// <summary>Current remaining balance.</summary>
        public double CurrentBalance => ReadSingleton().CurrentBalance;

        /// <summary>Withdrawal percentage set by player (0, 25, 50, 75, 100). Read-only - use CorruptionSchemeRequest to change.</summary>
        public int WithdrawPercent => ReadConfig().WithdrawPercent;

        /// <summary>Total amount withdrawn from fund.</summary>
        public double WithdrawnAmount => ReadSingleton().WithdrawnAmount;

        /// <summary>True if fund is depleted (2x disaster penalties).</summary>
        public bool IsDepleted => ReadSingleton().IsDepleted;

        /// <summary>Penalty multiplier for disasters when fund is low/empty.</summary>
        public float DisasterPenaltyMultiplier => ReadSingleton().DisasterPenaltyMultiplier;

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            EmergencyFundSingleton.EnsureExists(EntityManager);

            m_SingletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<EmergencyFundSingleton>()
            );

            m_ConfigQuery = GetEntityQuery(
                ComponentType.ReadOnly<EmergencyFundSettings>()
            );

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_EmergencyFundLookup = GetComponentLookup<EmergencyFundSingleton>(true);
            RefreshLiveSnapshotFromEntities();

            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.Income);
            SubscribeRequired<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);

            Log.Info($"{nameof(EmergencyFundSystem)} created (single-writer pattern)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EmergencyFundSingleton.EnsureExists(EntityManager);
            if (!m_HasRestoredEmergencyFund)
                RefreshLiveSnapshotFromEntities();
        }

        protected override void OnUpdateImpl()
        {
            // Lookup updated in OnDayChanged before use
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);
            UnsubscribeSafe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);

            Log.Info($"{nameof(EmergencyFundSystem)} destroyed");
            base.OnDestroy();
        }

        private void RefreshLiveSnapshotFromEntities()
        {
            if (m_HasRestoredEmergencyFund)
                return;

            m_LiveEmergencyFund = m_SingletonQuery.TryGetSingleton<EmergencyFundSingleton>(out var singleton)
                ? singleton
                : EmergencyFundSingleton.Default;
            m_LiveEmergencyFundSettings = m_ConfigQuery.TryGetSingleton<EmergencyFundSettings>(out var config)
                ? config
                : EmergencyFundSettings.Default;
        }

        // ============================================================================
        // CORE LOGIC
        // ============================================================================

        private void OnDayChanged(DayChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("EmergencyFund.OnDayChanged");
            if (!Enabled) return; // FIX W1-M1: Consistent with VIPProtectionRacketSystem
            if (m_DayDedup.IsProcessed(evt.DayNumber)) return;

            m_EmergencyFundLookup.Update(this);

            // Retry service resolution if it registered after OnCreate
            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            // Check wallet operational state — skip income request when wallet system is disabled (PreWar)
            // L-110: ShadowWalletSystem disables for acts < Crisis; income requests created while disabled are orphaned
            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber}: Withdrawal skipped - wallet not operational");
                return;
            }

            // Check frozen state via service (read-only)
            // Design doc: "All income/deductions blocked until unfreeze"
            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber}: Withdrawal skipped - assets frozen");
                return;
            }

            if (!m_SingletonQuery.TryGetSingleton<EmergencyFundSingleton>(out var singleton))
                return;

            // Read config from separate component (single-writer: CSRS)
            var config = ReadConfig();
            int withdrawPercent = config.WithdrawPercent;
            if (withdrawPercent <= 0)
            {
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            // Calculate target withdrawal based on percent
            double targetWithdrawn = singleton.InitialBalance * withdrawPercent / 100.0;

            // Gradually withdraw funds (50% of remaining difference per day)
            if (targetWithdrawn <= singleton.WithdrawnAmount)
            {
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            double dailyWithdraw = (targetWithdrawn - singleton.WithdrawnAmount) * BalanceConfig.Current.EmergencyFund.DailyWithdrawRate;
            // FIX W1-M11: Clamp before addition — prevents overshoot with DailyWithdrawRate >= 1.0
            dailyWithdraw = System.Math.Min(dailyWithdraw, targetWithdrawn - singleton.WithdrawnAmount);

            // Round BEFORE accumulation — WithdrawnAmount and shadow wallet use the same integer
            long roundedWithdraw = (long)System.Math.Round(dailyWithdraw, MidpointRounding.AwayFromZero);
            if (roundedWithdraw <= 0)
            {
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            // Transfer stolen funds to offshore account via ShadowIncomeRequest.
            // State and income request are queued through the same ECB producer.
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, roundedWithdraw, "EmergencyFund", $"EmergencyFund:{evt.DayNumber}"))
            {
                // Queue failed (wallet unavailable this frame): no ShadowIncomeApplied ack will
                // arrive for this day, so mark it here like the other terminal early-returns
                // above — otherwise the day is never marked and the withdrawal is silently lost
                // (DayChangedEvent fires once per day and never replays).
                Log.Warn($"Day {evt.DayNumber}: emergency-fund withdrawal queue failed (wallet unavailable), skipping");
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
        }

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            const string operationPrefix = "EmergencyFund:";
            if (!evt.OperationKey.StartsWith(operationPrefix, StringComparison.Ordinal))
                return;
            if (evt.Amount <= 0)
                return;
            EmergencyFundSingleton.EnsureExists(EntityManager);
            if (!m_SingletonQuery.TryGetSingletonEntity<EmergencyFundSingleton>(out var entity))
                return;

            var singleton = ReadSingleton();
            singleton.WithdrawnAmount = System.Math.Min(singleton.WithdrawnAmount + evt.Amount, singleton.InitialBalance);
            m_LiveEmergencyFund = singleton;
            EntityManager.SetComponentData(entity, singleton);
            if (int.TryParse(evt.OperationKey.Substring(operationPrefix.Length), out int dayNumber))
                m_DayDedup.MarkProcessed(dayNumber);

            EventBus?.SafePublish(new NarrativeTriggerEvent(
                NarrativeTrigger.ShockWithdraw.ToKey(),
                new System.Collections.Generic.Dictionary<string, string>
                {
                    { "0", evt.Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) }
                }),
                "EmergencyFundSystem");

            Log.Info($"Applied emergency fund withdrawal ${evt.Amount:N0} -> offshore, remaining: ${singleton.CurrentBalance:N0}");
        }
    }
}
