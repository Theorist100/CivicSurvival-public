using System;
using Game;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Fuel Siphoning: Steal fuel from city generators, sell on black market.
    ///
    /// Mechanics:
    /// - Player sets siphon percentage via CorruptionSchemeRequest (0%, 15%, 30%, 50%)
    /// - State stored in FuelSiphoningSingleton
    /// - Higher siphon = generators consume fuel faster
    /// - Daily income added to offshore account
    /// - Risk: generators run out during crisis
    ///
    /// Single-writer pattern: Uses ShadowIncomeRequest for wallet writes.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(FuelSiphoningSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class FuelSiphoningSystem : CivicSystemBase, IFuelSiphoningSettingsWriter, IFuelSiphoningStateReader, ICivicSingletonOwner<FuelSiphoningSingleton>
    {
        private static readonly LogContext Log = new("FuelSiphoning");

        // ============================================================================
        // ECS STATE
        // ============================================================================

        private EntityQuery m_SingletonQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private IShadowWalletService m_WalletService = null!;
        // Monotonic-tick dedup keyed by GLOBAL HOUR (day*24+hour) — the siphon pays
        // hourly slices. The struct only tracks "highest handled counter", so the
        // day-named type fits an hour counter; the field name carries the unit.
        private DayChangedDedup m_HourDedup = default;
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

        [System.NonSerialized] private bool m_SingletonMissingWarned;
        [System.NonSerialized] private FuelSiphoningSingleton m_LiveFuelSiphoning;

        // ============================================================================
        // PROPERTIES (read from singleton)
        // ============================================================================

        private FuelSiphoningSingleton ReadSingleton()
        {
            if (m_HasRestoredFuelSiphoning)
                return m_LiveFuelSiphoning;

            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_SingletonQuery.TryGetSingleton<FuelSiphoningSingleton>(out var singleton))
            {
                if (!m_SingletonMissingWarned)
                {
                    m_SingletonMissingWarned = true;
                    Log.Warn("FuelSiphoningSingleton missing — returning Default. Singleton entity may have been destroyed.");
                }
                return m_LiveFuelSiphoning;
            }
            m_LiveFuelSiphoning = singleton;
            return singleton;
        }

        /// <summary>Siphoning percentage (0, 15, 30, 50). Read-only - use CorruptionSchemeRequest to change.</summary>
        public int SiphonPercent => ReadSingleton().SiphonPercent;

        /// <summary>
        /// Fuel consumption multiplier applied to generators.
        /// 0% = 1.0x, 15% = 1.3x, 30% = 1.6x, 50% = 2.0x
        /// </summary>
        public float ConsumptionMultiplier => ReadSingleton().ConsumptionMultiplier;

        /// <summary>Daily income from siphoning.</summary>
        public double DailyIncome => ReadSingleton().DailyIncome;

        public bool TrySetFuelSiphonPercent(int percent)
        {
            FuelSiphoningSingleton.EnsureExists(EntityManager);
            if (!m_SingletonQuery.TryGetSingletonEntity<FuelSiphoningSingleton>(out var entity))
                return false;

            m_LiveFuelSiphoning = new FuelSiphoningSingleton { SiphonPercent = percent };
            EntityManager.SetComponentData(entity, m_LiveFuelSiphoning);
            Log.Info($"[FuelSiphoning] Set to {percent}%");
            return true;
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            FuelSiphoningSingleton.EnsureExists(EntityManager);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IFuelSiphoningSettingsWriter>(this);
                ServiceRegistry.Instance.Register<IFuelSiphoningStateReader>(this);
            }

            m_SingletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<FuelSiphoningSingleton>()
            );

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            RefreshLiveSnapshotFromEntities();
            SubscribeRequired<HourChangedEvent>(OnHourChanged);
            SubscribeRequired<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);

            Log.Info($"{nameof(FuelSiphoningSystem)} created (single-writer pattern)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            FuelSiphoningSingleton.EnsureExists(EntityManager);
            if (!m_HasRestoredFuelSiphoning)
                RefreshLiveSnapshotFromEntities();
        }

        protected override void OnUpdateImpl()
        {
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<HourChangedEvent>(OnHourChanged);
            UnsubscribeSafe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IFuelSiphoningSettingsWriter>(this);
                ServiceRegistry.Instance.Unregister<IFuelSiphoningStateReader>(this);
            }

            Log.Info($"{nameof(FuelSiphoningSystem)} destroyed");
            base.OnDestroy();
        }

        private void RefreshLiveSnapshotFromEntities()
        {
            if (m_HasRestoredFuelSiphoning)
                return;

            m_LiveFuelSiphoning = m_SingletonQuery.TryGetSingleton<FuelSiphoningSingleton>(out var singleton)
                ? singleton
                : FuelSiphoningSingleton.Default;
        }

        // ============================================================================
        // CORE LOGIC
        // ============================================================================

        /// <summary>
        /// Pay one hourly slice of the day's siphon income. DailyIncome is a fixed
        /// per-day amount, so the slice is a plain 24th and the daily total is
        /// unchanged — this is cadence: a single midnight payout meant a whole game
        /// day of an unchanged wallet after switching the scheme on.
        ///
        /// Sub-dollar slices are handled by AccumulateWithRemainder, so a small
        /// siphon does not round to zero every hour and silently pay nothing.
        /// </summary>
        private void OnHourChanged(HourChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("FuelSiphoning.OnHourChanged");
            if (!Enabled) return; // FIX W1-M1: Consistent with VIPProtectionRacketSystem

            int globalHour = HourTick.GlobalHour(evt);
            if (m_HourDedup.IsProcessed(globalHour)) return;

            var singleton = ReadSingleton();

            int siphonPercent = singleton.SiphonPercent;
            if (siphonPercent <= 0)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            // Retry service resolution if it registered after OnCreate
            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            // Check wallet operational state — skip income request when wallet system is disabled (PreWar)
            // L-110: ShadowWalletSystem disables for acts < Crisis; income requests created while disabled are orphaned
            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Income skipped - wallet not operational");
                return;
            }

            // Check frozen state via service (read-only)
            // Design doc: "All income/deductions blocked until unfreeze"
            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Income skipped - assets frozen");
                return;
            }

            double hourlyIncome = HourTick.SliceOfDaily(singleton.DailyIncome);
            long roundedIncome = GameRate.AccumulateWithRemainder(hourlyIncome, 1.0, ref m_IncomeRemainder);
            if (roundedIncome <= 0)
            {
                // Slice below $1 this hour — the remainder carries it to a later hour.
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            // Single-writer pattern: create ShadowIncomeRequest entity
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, roundedIncome, "FuelSiphoning", $"FuelSiphoning:{globalHour}"))
            {
                // Queue failed (wallet unavailable this frame): no ack will mark this hour, so
                // mark it here like the other terminal early-returns — otherwise the hour is
                // never marked and the income is silently skipped (HourChangedEvent never replays).
                // Return the slice to the remainder so it is not lost.
                m_IncomeRemainder = System.Math.Min(m_IncomeRemainder + roundedIncome, MAX_INCOME_CARRY);
                Log.Warn($"Day {evt.DayNumber} hour {evt.Hour}: fuel-siphon income queue failed (wallet unavailable), skipping");
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: queued +${roundedIncome:N0} to offshore");
        }

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            const string operationPrefix = "FuelSiphoning:";
            if (!evt.OperationKey.StartsWith(operationPrefix, StringComparison.Ordinal))
                return;
            if (evt.Amount <= 0)
                return;
            // Key carries the global hour (day*24+hour) since the payout went hourly.
            if (int.TryParse(evt.OperationKey.Substring(operationPrefix.Length), out int globalHour))
                m_HourDedup.MarkProcessed(globalHour);
            if (Log.IsDebugEnabled) Log.Debug($"Hour {globalHour}: applied +${evt.Amount:N0} to offshore");
        }
    }
}
