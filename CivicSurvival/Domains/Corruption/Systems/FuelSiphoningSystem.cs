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
        private DayChangedDedup m_DayDedup = default;
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
            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.Income);
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
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);
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

        private void OnDayChanged(DayChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("FuelSiphoning.OnDayChanged");
            if (!Enabled) return; // FIX W1-M1: Consistent with VIPProtectionRacketSystem

            if (m_DayDedup.IsProcessed(evt.DayNumber)) return;

            var singleton = ReadSingleton();

            int siphonPercent = singleton.SiphonPercent;
            if (siphonPercent <= 0)
            {
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            // Retry service resolution if it registered after OnCreate
            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            // Check wallet operational state — skip income request when wallet system is disabled (PreWar)
            // L-110: ShadowWalletSystem disables for acts < Crisis; income requests created while disabled are orphaned
            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber}: Income skipped - wallet not operational");
                return;
            }

            // Check frozen state via service (read-only)
            // Design doc: "All income/deductions blocked until unfreeze"
            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber}: Income skipped - assets frozen");
                return;
            }

            double income = singleton.DailyIncome;
            long roundedIncome = (long)System.Math.Round(income);
            if (roundedIncome <= 0)
            {
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            // Single-writer pattern: create ShadowIncomeRequest entity
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, roundedIncome, "FuelSiphoning", $"FuelSiphoning:{evt.DayNumber}"))
            {
                // Queue failed (wallet unavailable this frame): no ack will mark this day, so
                // mark it here like the other terminal early-returns — otherwise the day is
                // never marked and the income is silently skipped (DayChangedEvent never replays).
                Log.Warn($"Day {evt.DayNumber}: fuel-siphon income queue failed (wallet unavailable), skipping");
                m_DayDedup.MarkProcessed(evt.DayNumber);
                return;
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            Log.Info($"Day {evt.DayNumber}: queued +${income:N0} to offshore");
        }

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            const string operationPrefix = "FuelSiphoning:";
            if (!evt.OperationKey.StartsWith(operationPrefix, StringComparison.Ordinal))
                return;
            if (evt.Amount <= 0)
                return;
            if (int.TryParse(evt.OperationKey.Substring(operationPrefix.Length), out int dayNumber))
                m_DayDedup.MarkProcessed(dayNumber);
            Log.Info($"Day {dayNumber}: applied +${evt.Amount:N0} to offshore");
        }
    }
}
