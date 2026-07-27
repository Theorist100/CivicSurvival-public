using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Diplomacy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Features.Population;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Diplomacy.Systems
{
    /// <summary>
    /// Monitors city crisis level (% population in blackout).
    /// Updates once per day for performance.
    ///
    /// Publishes state via CrisisStateSingleton for consumers (DonorConferenceSystem, UI).
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(CrisisStateSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
#pragma warning disable CIVIC223 // PostLoadValidationSystem invokes ICivicSingletonOwner.OnLoadRestore; Deserialize only buffers payload.
    public partial class CrisisMonitorSystem : CivicSystemBase, IDefaultSerializable, IResettable,
        IPostLoadValidation, ICivicSingletonOwner<CrisisStateSingleton>, IBootDefaultsReset
#pragma warning restore CIVIC223
    {
        private static readonly LogContext Log = new("CrisisMonitorSystem");
        // Cached metrics (updated daily)
        private float m_CrisisLevel;
        // The two sides of the persisted percentage, each a measurement taken on a past game day
        // and kept. They carry their counter for the same reason the scenario measurements do:
        // a restored pair from a build whose total came from a citywide scalar while its
        // affected tally came from the selection is not a percentage of anything, and the stamp
        // is what says so instead of the numbers quietly reading a few points high.
        private RecordedPopulation m_TotalPopulation;
        private RecordedPopulation m_AffectedPopulation;
        private int m_LastUpdateDay;

        /// <summary>
        /// The counter both numbers are taken with: the sum over the published household
        /// selection, which is where the blackout tally is looked up household by household.
        /// </summary>
        private const PopulationMeasure CityMeasure = PopulationMeasure.ResidentSelection;
        // Numerator and denominator of the crisis percentage both come from this one view:
        // the blackout tally is summed per household out of the published selection, so the
        // total it is divided by must be the sum over that same selection. Holding the
        // population scalar here would put a citywide count over a sampled denominator and
        // bias the persisted crisis level upward — the exact defect this system must not carry.
        [NonSerialized] private IResidentHouseholdView m_ResidentHouseholdView = null!;
        [NonSerialized] private int m_ResidentHouseholdObserverVersion;

        // ECS queries
        private EntityQuery m_BlackoutResidentialQuery;
        private EntityQuery m_CurrentActQuery;

        // Cached lookups (Axiom 8: cache in OnCreate, Update before use in OnDayChanged handler)
#pragma warning disable CIVIC185 // Event-driven: Updated in CountResidentialPopulation() called from OnDayChanged, not OnUpdateImpl
        private BufferLookup<Renter> m_RenterLookup;
#pragma warning restore CIVIC185

        // ECS singleton — liveness-validated handle (Inv 2; CIVIC427)
        [NonSerialized] private CivicSingletonHandle<CrisisStateSingleton> m_Singleton;
        [NonSerialized] private bool m_RecalculateAfterLoad;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Cache queries for population counting
            // BUG-DOM-036 FIX: Exclude destroyed buildings from population count
            m_BlackoutResidentialQuery = GetEntityQuery(
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<Renter>(),
                ComponentType.ReadOnly<BlackoutState>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Deleted>()
            );
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            // Cache lookups (Axiom 8)
            m_RenterLookup = GetBufferLookup<Renter>(true);

            m_Singleton = CreateSingletonHandle<CrisisStateSingleton>();
            EnsureSingletonEntity(EntityManager);
            UpdateSingleton();

            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.StateChange);

            Log.Info($"{nameof(CrisisMonitorSystem)} created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_ResidentHouseholdView ??= ServiceRegistry.Instance.Require<IResidentHouseholdView>();
        }

        protected override void OnUpdateImpl()
        {
            // No per-frame logic - using DayChangedEvent
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);

            // Clean up ECS singleton
            if (m_Singleton.Entity != Entity.Null && EntityManager.Exists(m_Singleton.Entity))
            {
                EntityManager.DestroyEntity(m_Singleton.Entity);
            }

            Log.Info($"{nameof(CrisisMonitorSystem)} destroyed");
            base.OnDestroy();
        }

        private void OnDayChanged(DayChangedEvent evt)
        {
            if (!IsCrisisOrLater()) return;

            using var _ = PerformanceProfiler.Measure("CrisisMonitor.OnDayChanged");
            if (evt.DayNumber <= m_LastUpdateDay)
                return;

            m_LastUpdateDay = evt.DayNumber;
            UpdateCrisisMetrics();
        }

        private bool IsCrisisOrLater()
        {
            return m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var act)
                && act.CurrentAct >= Act.Crisis;
        }

        private void UpdateCrisisMetrics()
        {
            // OnStartRunning is canonical; this re-resolves when
            // ValidateAfterLoad runs before OnStartRunning in PLVS sequencing.
            m_ResidentHouseholdView ??= ServiceRegistry.Instance.Require<IResidentHouseholdView>();

            // Readiness gate (A7). BOTH sides of the percentage come out of the household
            // selection — the affected tally is looked up per household, the total is the
            // sum over that same selection — so SelectionReady is the gate. It is strictly
            // stronger than the scalar gate this used to stand on, and that matters after a
            // load: the restored scalar is ready immediately while the selection is still
            // rebuilding, so the old gate let a tick through that divided a real total by an
            // empty selection and wrote a false 0%. Not selection-ready: skip without
            // touching the persisted m_CrisisLevel — the next DayChanged retries and the
            // restored level survives until then.
            if (!m_ResidentHouseholdView.IsSelectionReady)
            {
                LogCrisisDecision(wrote: false);
                return;
            }

            var residentSnapshot = m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion).Value;
            // Both numbers are stamped in the same breath, from the same observation, so the
            // pair cannot end up half on one counter and half on another.
            int affectedPopulation = CountAffectedResidentialPopulation(m_BlackoutResidentialQuery, residentSnapshot);
            int totalPopulation = residentSnapshot.AliveCitizensInSelection;
            m_AffectedPopulation = RecordedPopulation.On(CityMeasure, affectedPopulation);
            m_TotalPopulation = RecordedPopulation.On(CityMeasure, totalPopulation);

            if (totalPopulation == 0)
            {
                m_CrisisLevel = 0f;
            }
            else
            {
                m_CrisisLevel = Math.Min(100f, (affectedPopulation * 100f) / totalPopulation);
            }

            // Update ECS singleton
            UpdateSingleton();

            LogCrisisDecision(wrote: true);
            if (Log.IsDebugEnabled) Log.Debug($"[CrisisMonitor] Day {m_LastUpdateDay}: Crisis {m_CrisisLevel:F1}% ({affectedPopulation}/{totalPopulation} on {CityMeasure})");
        }

        // [POP-READY] crisis-decision log (Verification table): proves CrisisMonitor never
        // writes a false 0% on pop=0 after load — a skipped (not-ready) tick logs wrote=false
        // and leaves the restored crisis level untouched.
        private void LogCrisisDecision(bool wrote)
        {
            if (!Log.IsDebugEnabled)
                return;

            // Both call sites sit below the Require-resolve in UpdateCrisisMetrics, and Require
            // fails loud, so the view is read directly. A missing view must not print as an
            // ordinary not-ready tick in the very ledger that exists to prove why a tick was
            // skipped and that the restored crisis level was left alone.
            bool selectionReady = m_ResidentHouseholdView.IsSelectionReady;
            Log.Info($"[POP-READY] Crisis day={m_LastUpdateDay} selectionReady={selectionReady} pop={m_TotalPopulation.Value} on {m_TotalPopulation.Measure} crisis={m_CrisisLevel:F1}% wrote={wrote}");
        }

        private void UpdateSingleton()
        {
            EnsureSingletonEntity(EntityManager);

            EntityManager.SetComponentData(m_Singleton.Entity, new CrisisStateSingleton
            {
                CrisisLevel = m_CrisisLevel
            });
        }

        private void EnsureSingletonEntity(EntityManager entityManager)
        {
            // EntityManager-based EnsureSingleton: called from the OnDayChanged
            // event handler and ICivicSingletonOwner.OnLoadRestore where this
            // system's SystemAPI context is not valid (CIVIC292/185). Canonical
            // Inv-2 contract (liveness → query-first → dedup → create-if-absent)
            // is centralized in CivicSystemBase.
            EnsureSingleton(ref m_Singleton, entityManager, CrisisStateSingleton.Default);
        }

        /// <summary>
        /// Count resident population in blacked-out residential buildings.
        /// </summary>
        private int CountAffectedResidentialPopulation(EntityQuery query, ResidentHouseholdSnapshot residentSnapshot)
        {
            int population = 0;

            using var entities = query.ToEntityArray(Allocator.Temp);
            m_RenterLookup.Update(this);

            var liveResidentsByHousehold = BuildLiveResidentsByHousehold(residentSnapshot, Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!EntityManager.IsComponentEnabled<BlackoutState>(entity))
                        continue;

                    if (!m_RenterLookup.TryGetBuffer(entity, out var renters))
                        continue;

                    foreach (var renter in renters)
                    {
                        long householdKey = MakeHouseholdKey(renter.m_Renter.Index, renter.m_Renter.Version);
                        if (liveResidentsByHousehold.TryGetValue(householdKey, out int liveResidents))
                            population += liveResidents;
                    }
                }
            }
            finally
            {
                if (liveResidentsByHousehold.IsCreated)
                    liveResidentsByHousehold.Dispose();
            }

            return population;
        }

        private static NativeParallelHashMap<long, int> BuildLiveResidentsByHousehold(
            ResidentHouseholdSnapshot snapshot,
            Allocator allocator)
        {
            int capacity = Math.Max(1, snapshot.EligibleHouseholds.Length);
            var result = new NativeParallelHashMap<long, int>(capacity, allocator);

            int count = Math.Min(snapshot.EligibleHouseholds.Length, snapshot.LiveCitizensPerHousehold.Length);
            for (int i = 0; i < count; i++)
            {
                Entity household = snapshot.EligibleHouseholds[i];
                result.TryAdd(MakeHouseholdKey(household.Index, household.Version), snapshot.LiveCitizensPerHousehold[i]);
            }

            return result;
        }

        private static long MakeHouseholdKey(int index, int version)
            => ((long)index << 32) ^ (uint)version;

        // ============================================================================
        // IResettable
        // ============================================================================

        public void ResetState()
        {
            m_CrisisLevel = 0f;
            m_TotalPopulation = RecordedPopulation.NotRecorded;
            m_AffectedPopulation = RecordedPopulation.NotRecorded;
            m_LastUpdateDay = 0;
            m_ResidentHouseholdObserverVersion = 0;
            m_RecalculateAfterLoad = false;
            m_Singleton.Invalidate();
        }

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void OnLoadRestore(EntityManager entityManager)
        {
            EnsureSingletonEntity(entityManager);
            entityManager.SetComponentData(m_Singleton.Entity, new CrisisStateSingleton
            {
                CrisisLevel = m_CrisisLevel
            });
        }

        public int HydrationOrder => HydrationPriority.DEFAULT;

        public void ValidateAfterLoad()
        {
            if (!m_RecalculateAfterLoad)
            {
                UpdateSingleton();
                return;
            }

            m_RecalculateAfterLoad = false;
            if (!IsCrisisOrLater())
            {
                UpdateSingleton();
                return;
            }

            int savedLastUpdateDay = m_LastUpdateDay;
            UpdateCrisisMetrics();
            m_LastUpdateDay = Math.Max(0, savedLastUpdateDay - 1);
        }

        // ============================================================================
        // Serialization
        // ============================================================================

        public void SetDefaults(Context context)
        {
            ResetState();
            UpdateSingleton();
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                var state = new CrisisMonitorPersistState(
                    m_CrisisLevel,
                    m_TotalPopulation,
                    m_AffectedPopulation,
                    m_LastUpdateDay);
                CrisisMonitorCodec.Write(state, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CrisisMonitorSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(CrisisMonitorSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CrisisMonitorCodec.Read(reader, out var state);
                m_CrisisLevel = state.CrisisLevel;
                m_TotalPopulation = state.TotalPopulation;
                m_AffectedPopulation = state.AffectedPopulation;
                m_LastUpdateDay = state.LastUpdateDay;

                // PLVS calls ValidateAfterLoad after RestoreSingletonOwners; defer
                // the act decision until then. Sibling Deserialize order is undefined.
                m_RecalculateAfterLoad = true;
                UpdateSingleton();

                Log.Info($"[{nameof(CrisisMonitorSystem)}] Deserialized: Crisis={m_CrisisLevel:F1}%, Pop={m_AffectedPopulation.Value}/{m_TotalPopulation.Value} on {m_TotalPopulation.Measure}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
