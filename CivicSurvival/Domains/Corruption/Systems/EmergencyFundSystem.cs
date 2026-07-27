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
        // Monotonic-tick dedup, now keyed by GLOBAL HOUR (day*24+hour) rather than
        // day: the raid pays hourly slices. The struct only tracks "highest handled
        // counter", so the day-named type fits an hour counter unchanged; the field
        // name carries the actual unit.
        private DayChangedDedup m_HourDedup = default;
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

            SubscribeRequired<HourChangedEvent>(OnHourChanged);
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
            UnsubscribeSafe<HourChangedEvent>(OnHourChanged);
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

        /// <summary>
        /// Withdraw one hourly slice of the raid. Paid per game hour rather than in a
        /// single midnight lump: a whole game day (~15 real minutes at 1x) of an
        /// unchanged wallet after switching the raid on read as "nothing works".
        ///
        /// The per-day total is unchanged. The rate applies to the REMAINING gap, so
        /// slicing compounds (see <see cref="GameRate.CompoundingRatePerHour"/>) —
        /// dividing the daily rate by 24 would quietly withdraw less than configured.
        /// </summary>
        private void OnHourChanged(HourChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("EmergencyFund.OnHourChanged");
            if (!Enabled) return; // FIX W1-M1: Consistent with VIPProtectionRacketSystem
            int globalHour = HourTick.GlobalHour(evt);
            if (m_HourDedup.IsProcessed(globalHour)) return;

            m_EmergencyFundLookup.Update(this);

            // Retry service resolution if it registered after OnCreate
            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            // Check wallet operational state — skip income request when wallet system is disabled (PreWar)
            // L-110: ShadowWalletSystem disables for acts < Crisis; income requests created while disabled are orphaned
            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Withdrawal skipped - wallet not operational");
                return;
            }

            // Check frozen state via service (read-only)
            // Design doc: "All income/deductions blocked until unfreeze"
            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Withdrawal skipped - assets frozen");
                return;
            }

            if (!m_SingletonQuery.TryGetSingleton<EmergencyFundSingleton>(out var singleton))
                return;

            // Read config from separate component (single-writer: CSRS)
            var config = ReadConfig();
            int withdrawPercent = config.WithdrawPercent;
            if (withdrawPercent <= 0)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            // Calculate target withdrawal based on percent
            double targetWithdrawn = singleton.InitialBalance * withdrawPercent / 100.0;

            // Gradually withdraw funds (DailyWithdrawRate of the remaining gap per day)
            if (targetWithdrawn <= singleton.WithdrawnAmount)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            double remainingGap = targetWithdrawn - singleton.WithdrawnAmount;
            double hourlyRate = GameRate.CompoundingRatePerHour(BalanceConfig.Current.EmergencyFund.DailyWithdrawRate);
            double hourlyWithdraw = remainingGap * hourlyRate;
            // FIX W1-M11: Clamp before addition — prevents overshoot with DailyWithdrawRate >= 1.0
            hourlyWithdraw = System.Math.Min(hourlyWithdraw, remainingGap);

            // Round BEFORE accumulation — WithdrawnAmount and shadow wallet use the same integer
            long roundedWithdraw = (long)System.Math.Round(hourlyWithdraw, MidpointRounding.AwayFromZero);
            if (roundedWithdraw <= 0)
            {
                // Slice rounds to zero (gap nearly closed): mark the hour so the tail
                // does not re-evaluate every hour, and let the next day's gap grow.
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            // Transfer stolen funds to offshore account via ShadowIncomeRequest.
            // State and income request are queued through the same ECB producer.
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, roundedWithdraw, "EmergencyFund", $"EmergencyFund:{globalHour}"))
            {
                // Queue failed (wallet unavailable this frame): no ShadowIncomeApplied ack will
                // arrive for this hour, so mark it here like the other terminal early-returns
                // above — otherwise the hour is never marked and the withdrawal is silently lost
                // (HourChangedEvent fires once per hour and never replays).
                Log.Warn($"Day {evt.DayNumber} hour {evt.Hour}: emergency-fund withdrawal queue failed (wallet unavailable), skipping");
                m_HourDedup.MarkProcessed(globalHour);
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
            // Key carries the global hour (day*24+hour) since the raid went hourly.
            if (int.TryParse(evt.OperationKey.Substring(operationPrefix.Length), out int globalHour))
                m_HourDedup.MarkProcessed(globalHour);

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
