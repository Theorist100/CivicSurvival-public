using System;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Corruption;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Updates CorruptionSingleton by aggregating data from multiple singletons.
    /// Chain: ShadowTradeDailySystem → this → CountermeasuresUpdateSystem.
    /// Runs after ShadowTradeDaily (fresh ShadowExportState) and before Countermeasures (fresh CorruptionSingleton).
    ///
    /// Also handles CorruptionGainEvent - accumulates exposure from kickbacks,
    /// shadow trade, and disaster contracts. Exposure decays over time.
    /// </summary>
    [SingletonOwner(typeof(CorruptionSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.ReconcileAfterLoad,
        DisposePhase = SingletonLifecyclePhase.None)]
    [ActIndependent]
    public partial class CorruptionStateUpdateSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("CorruptionStateUpdateSystem");

        // Phase-align with MCS so RegisterAfter(MCS)] is always respected (both tick together)
        protected override string ThrottlePhaseKey => MaintenanceContractReadyMarker.PHASE_KEY;

        private EntityQuery m_ShadowExportQuery;
        private EntityQuery m_ShadowWalletQuery;
        private EntityQuery m_EmergencyFundQuery;
        private EntityQuery m_FuelSiphoningQuery;
        private EntityQuery m_ContractStatsQuery;
        private IDistrictStateReader? m_DistrictState;
        private GameTimeSystem? m_TimeProvider;
        private readonly CorruptionStateInputSnapshotPublisher m_InputSnapshotPublisher = new();
        private readonly VersionedView<CorruptionStateInputSnapshot> m_InputSnapshotView = new(CorruptionStateInputSnapshot.Empty);
        private int m_InputSnapshotObserverCursor;
        // S12a-2 ACCEPTED: Not serialized — guarded by <0 re-init in GetDayFraction().
        // After load, first update sets to current hours and returns 0f (no decay applied).
        [System.NonSerialized] private double m_PrevGameHours = -1.0;
        // S12a-3 ACCEPTED: Not serialized — at most 1s of pending events lost on load.
        // Exposure is gradual accumulation (kickbacks, shadow trade), negligible loss.
        private float m_PendingExposure;
        private readonly object m_Lock = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CorruptionSingletonQuery = GetEntityQuery(ComponentType.ReadWrite<CorruptionSingleton>());
            m_ShadowExportQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowExportState>());
            m_ShadowWalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());
            m_EmergencyFundQuery = GetEntityQuery(ComponentType.ReadOnly<EmergencyFundSingleton>());
            m_FuelSiphoningQuery = GetEntityQuery(ComponentType.ReadOnly<FuelSiphoningSingleton>());
            m_ContractStatsQuery = GetEntityQuery(ComponentType.ReadOnly<ContractStatsSingleton>());

            // Pre-tick consumers (e.g. MobilizationSystem in GameSimulation) can read
            // CorruptionSingleton before this system gets its first Update — so the
            // create must happen here, not in OnStartRunning. EnsureExists is idempotent,
            // and the Serialization partial re-calls it on save-load (CIVIC414).
            CorruptionSingleton.EnsureExists(EntityManager);

            SubscribeRequired<CorruptionGainEvent>(OnCorruptionGain);
            Log.Info("Created, subscribed to CorruptionGainEvent");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<CorruptionGainEvent>(OnCorruptionGain);
            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Cache dependencies
            m_DistrictState = ServiceRegistry.Instance.Require<IDistrictStateReader>();
            m_TimeProvider = GameTimeSystem.Instance;

            CorruptionSingleton.EnsureExists(EntityManager);
        }

        private void OnCorruptionGain(CorruptionGainEvent evt)
        {
            if (evt.Amount <= 0f)
            {
                Log.Warn($"Rejected non-positive corruption gain {evt.Amount:F1} from {evt.Source}");
                return;
            }

            ForceNextUpdate();
            lock (m_Lock)
            {
                m_PendingExposure += evt.Amount;
            }
            if (Log.IsDebugEnabled) Log.Debug($"+{evt.Amount:F1} exposure from {evt.Source}");
        }

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        protected override void OnThrottledUpdate()
        {
            using (PerformanceProfiler.Measure("SP:CSUS.InputSnapshotPublish"))
            {
                m_InputSnapshotPublisher.Publish(
                    m_InputSnapshotView,
                    m_ShadowExportQuery,
                    m_ShadowWalletQuery,
                    m_EmergencyFundQuery,
                    m_FuelSiphoningQuery,
                    m_ContractStatsQuery,
                    m_DistrictState);
            }

            if (!m_CorruptionSingletonQuery.TryGetSingletonRW<CorruptionSingleton>(out var stateRef))
                return;

            ref var state = ref stateRef.ValueRW;
            if (m_RestoredExposure > 0f)
            {
                state.AccumulatedExposure = m_RestoredExposure;
                Log.Info($"Restored AccumulatedExposure={m_RestoredExposure:F1} on retry");
                m_RestoredExposure = 0f;
            }

            // Apply pending exposure from events
            float pendingToApply;
            lock (m_Lock)
            {
                pendingToApply = m_PendingExposure;
                m_PendingExposure = 0f;
            }

            if (pendingToApply > 0f)
            {
                state.AccumulatedExposure += pendingToApply;
                if (Log.IsDebugEnabled) Log.Debug($"Applied {pendingToApply:F1} exposure, total: {state.AccumulatedExposure:F1}");
            }

            // Apply decay (per frame, scaled by day fraction)
            float dayFraction = GetDayFraction();
            float decayRate = BalanceConfig.Current.Corruption.ExposureDecayPerDay;
            float decay = decayRate * dayFraction;

            if (state.AccumulatedExposure > 0f && decay > 0f)
            {
                state.AccumulatedExposure = Math.Max(0f, state.AccumulatedExposure - decay);
            }

            var inputSnapshot = m_InputSnapshotView.Observe(ref m_InputSnapshotObserverCursor).Value;
            ApplyInputSnapshot(ref state, inputSnapshot);
        }

        private static void ApplyInputSnapshot(ref CorruptionSingleton state, CorruptionStateInputSnapshot input)
        {
            if (input.HasShadowExport)
                state.ExportPercentage = input.ExportPercentage;

            if (input.HasShadowWallet)
                state.OffshoreBalance = input.OffshoreBalance;

            if (input.HasEmergencyFund)
                state.EmergencyFundWithdrawn = (double)input.EmergencyFundWithdrawn;

            if (input.HasFuelSiphoning)
                state.FuelSiphonPercent = input.FuelSiphonPercent;

            if (input.HasContractStats)
                state.ShadyContractCount = input.ShadyContractCount;

            if (input.HasDistrictState)
            {
                state.VIPDistrictCount = input.VIPDistrictCount;
                state.VIPBypassCount = input.VIPBypassCount;
            }
        }

        private float GetDayFraction()
        {
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                Log.Error("[CorruptionStateUpdateSystem] GameTimeSystem unavailable — skipping update");
                return 0f;
            }
            double currentHours = m_TimeProvider.Current.TotalGameHours;

            if (m_PrevGameHours < 0.0)
            {
                m_PrevGameHours = currentHours;
                return 0f;
            }

            float deltaHours = (float)Math.Max(0.0, currentHours - m_PrevGameHours);
            m_PrevGameHours = currentHours;
            return GameRate.DayFractionFromHours(deltaHours);
        }

    }
}
