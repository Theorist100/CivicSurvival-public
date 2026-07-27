using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using Colossal.Collections;
using Game.Agents;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CivicSurvival.Core.Features.Population
{
    [ActIndependent]
    public sealed partial class ResidentPopulationModelSystem :
        ThrottledSystemBase,
        IResidentHouseholdView,
        IResidentPopulationEligibilityView,
        ICityPopulationReader,
        IPostLoadValidation
    {
        private static readonly LogContext Log = new("ResidentPopulationModel");

        private const int INITIAL_HOUSEHOLD_CAPACITY = 1024;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        private EntityQuery m_HouseholdQuery;
        // Vanilla city population record, read by the ICityPopulationReader pair below.
        private EntityQuery m_CityPopulationQuery;
        private EntityTypeHandle m_EntityType;
        private BufferTypeHandle<HouseholdCitizen> m_HouseholdCitizenType;
        private ComponentLookup<Citizen> m_CitizenLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<HealthProblem> m_HealthProblemLookup;

        // --- Selection ring ---
        // The ring is the PERMANENT owner of all three list pairs (allocated OnCreate,
        // disposed only OnDestroy). Snapshots BORROW a pair; FlattenSelectionJob writes
        // the Staging pair inside the job graph. Returns go through an identity check
        // (state + borrow version), so a double/foreign return is a structural no-op —
        // a future consumer holding a stale snapshot copy cannot silently hand a live
        // pair back to the ring: protection by construction, not convention.
        private const int SELECTION_SLOT_COUNT = 3;
        private const int SELECTION_SLOT_NONE = -1;

        private enum SelectionSlotState : byte
        {
            Free = 0,
            Staging = 1,
            Borrowed = 2
        }

        private struct SelectionSlot
        {
            public NativeList<Entity> Households;
            public NativeList<int> LiveCitizens;
            public SelectionSlotState State;
            /// <summary>Version of the snapshot currently borrowing this slot — the
            /// identity token for <see cref="ReturnBorrowedSlot"/>.</summary>
            public int BorrowVersion;
            /// <summary>Sum of <see cref="LiveCitizens"/> for the selection currently in
            /// this slot, stamped by <see cref="PublishSnapshots"/> from the same job
            /// result the pair was flattened from. Lives on the SLOT, not in a system
            /// field, so the ack re-publish (<see cref="RepublishHouseholdSnapshotSamePair"/>)
            /// re-reads the number that belongs to the pair it re-publishes instead of
            /// whatever the last rotation happened to leave behind.</summary>
            public int AliveCitizens;
        }

        // Lifetime containers (allocated OnCreate, disposed OnDestroy); contents are
        // transient — ResetTransientLoadState frees every slot and the post-load rebuild
        // refills the selection, so nothing here is ever persisted.
        [NonSerialized] private SelectionSlot[] m_SelectionSlots = System.Array.Empty<SelectionSlot>();
        // Slot the in-flight FlattenSelectionJob writes; NONE when nothing is scheduled.
        [NonSerialized] private int m_StagingSlot = SELECTION_SLOT_NONE;

        // Transient double-buffer toggle for the eligibility hashset; never persisted.
        // Reset on load (the published set is rebuilt after load), so saving it would pin
        // the next city to the previous city's buffer parity.
        [NonSerialized] private bool m_PublishA;

        // Per-fire native accumulator; cleared every cycle and rebuilt by the chunk job.
        // Pure transient — its only result is the alive-citizen sum stamped onto the
        // published selection slot, which is rebuilt after load and never persisted.
        [NonSerialized] private NativeAccumulator<ResidentPopulationData> m_Accumulator;
        private NativeParallelHashSet<Entity> m_EligibilityA;
        private NativeParallelHashSet<Entity> m_EligibilityB;
        // Single live-counts map: its writer (CountResidentPopulationJob) and its only
        // reader (FlattenSelectionJob) sit in the same dependency chain, so the former
        // published/back double-buffer of the map had no reader left and was collapsed.
        // The eligibility HASHSET keeps its double-buffer — consumers read the published
        // side through jobs (AddEligibilityReader / WellbeingResolver).
        private NativeParallelHashMap<Entity, int> m_LiveCounts;
#pragma warning disable CIVIC150 // In-flight transient job output; ValidateAfterLoad discards/rebuilds it after load.
        [NonSerialized] private bool m_HasScheduledResult;
        [NonSerialized] private bool m_WasReset;
#pragma warning restore CIVIC150

        [NonSerialized] private JobHandle m_EligibilityReadDependencies;
        [NonSerialized] private JobHandle m_ModelWriteDependencies;

        // Single source of truth for readiness (A1). Read through the views; never
        // duplicated into a snapshot struct. Set at every publish site;
        // serialization/reset of this latch on load is not done here.
        [NonSerialized] private ResidentPopulationReadiness m_Readiness = ResidentPopulationReadiness.NotReady;
        // True only for the duration of ValidateAfterLoad so a [POP-READY] publish made
        // from inside it (the empty-city route) is attributed to source=seed rather than
        // to a steady-state empty tick. It cannot reach the job-completion publish site:
        // that one runs a throttle tick later, after the finally below has cleared this.
        [NonSerialized] private bool m_InLoadSeed;
        // Cached for the [POP-READY] contour so pause is provable from log content
        // (selectedSpeed==0) rather than metadata. Read-only diagnostic use.
        [NonSerialized] private SimulationSystem m_SimulationSystem = null!;

#pragma warning disable CIVIC150 // Derived transient snapshots; ValidateAfterLoad rebuilds them after load.
        [NonSerialized] private ResidentHouseholdSnapshot m_HouseholdSnapshot;
        [NonSerialized] private ResidentHouseholdSnapshot m_PreviousHouseholdSnapshot;
        [NonSerialized] private int m_PendingDayChanges;
#pragma warning restore CIVIC150
        [VersionedViewSelfImpl("Resident household snapshot is published over a native selection ring the managed VersionedView cannot own; pending-day acknowledgements only advance the household cursor.")]
        [NonSerialized] private int m_HouseholdVersion;

        public int Version => m_HouseholdVersion;

        // Readiness is read at the view layer, backed by the single source field
        // m_Readiness (A1). One thing is published, so one gate answers for it.
        public bool IsSelectionReady => m_Readiness >= ResidentPopulationReadiness.SelectionReady;

        public NativeParallelHashSet<Entity>.ReadOnly EligibleHouseholds
        {
            get
            {
                ref NativeParallelHashSet<Entity> eligibility = ref GetCurrentPublishedEligibility();
                return eligibility.AsReadOnly();
            }
        }

        // --- ICityPopulationReader: the vanilla city population record ---
        //
        // Both accessors answer from one query read of Game.City.Population on the city
        // entity. They are explicit interface implementations on purpose: the contract is the
        // only way in, so no consumer can reach the numbers by holding the concrete system.
        //
        // Pause (Axiom 14): the answer does not depend on this system having ticked. The
        // producer lives in GameSimulation, which is frozen in pause, but the query is a live
        // ECS object that needs no refresh, so consumers in the pause-live phases (UIUpdate,
        // ModificationEnd) get the same answer they would get running. The value itself is
        // frozen in pause because vanilla's writer is frozen too — frozen, not wrong: nobody
        // moves in or dies while the simulation is stopped.
        //
        // The read waits on the vanilla Population writer (TryGetSingleton completes the write
        // dependency for the type). That job is scheduled once per 16 simulation ticks and is
        // long finished by the time anyone asks, so the wait is dependency bookkeeping.

        bool ICityPopulationReader.TryGetResidentCount(out int residents)
        {
            if (!m_CityPopulationQuery.TryGetSingleton<global::Game.City.Population>(out var cityPopulation))
            {
                // No city entity — the question has no answer. Zero here would be a lie;
                // zero WITH a city entity (below) is the truth about an empty city.
                residents = 0;
                return false;
            }

            residents = cityPopulation.m_Population;
            return true;
        }

        bool ICityPopulationReader.TryGetCityAverages(out int happiness, out int health)
        {
            if (!m_CityPopulationQuery.TryGetSingleton<global::Game.City.Population>(out var cityPopulation))
            {
                // Zero, not the neutral 50, so that a refusal stays distinguishable from a
                // genuine neutral reading even for a caller that drops the bool.
                happiness = 0;
                health = 0;
                return false;
            }

            // Passed through verbatim. On an empty city vanilla parks both at a neutral 50 and
            // that neutral is a real reading — rewriting it to zero is the defect that ruled
            // out the managed CountHouseholdDataSystem accessors as the source of this pair.
            happiness = cityPopulation.m_AverageHappiness;
            health = cityPopulation.m_AverageHealth;
            return true;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

#pragma warning disable CIVIC340 // Resident model intentionally counts households where tourist/commuter tags are absent or disabled.
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<HouseholdCitizen>(),
                ComponentType.Exclude<MovingAway>(),
                ComponentType.Exclude<TouristHousehold>(),
                ComponentType.Exclude<CommuterHousehold>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
#pragma warning restore CIVIC340

            // The vanilla record is reached through a query rather than a ComponentLookup:
            // a query answers without a per-frame refresh, and the two accessors below are
            // called from other systems and from non-ECS code, where .Update(this) would sync
            // a lookup against THIS system's state (Axiom 8 keeps .Update(this) in OnUpdate for
            // exactly that reason). See the accessor block for the pause consequence.
            m_CityPopulationQuery = GetEntityQuery(ComponentType.ReadOnly<global::Game.City.Population>());

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_EntityType = GetEntityTypeHandle();
            m_HouseholdCitizenType = GetBufferTypeHandle<HouseholdCitizen>(true);
            m_CitizenLookup = GetComponentLookup<Citizen>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_HealthProblemLookup = GetComponentLookup<HealthProblem>(true);

            m_SelectionSlots = new SelectionSlot[SELECTION_SLOT_COUNT];
            for (int i = 0; i < SELECTION_SLOT_COUNT; i++)
            {
                m_SelectionSlots[i] = new SelectionSlot
                {
                    Households = new NativeList<Entity>(INITIAL_HOUSEHOLD_CAPACITY, Allocator.Persistent),
                    LiveCitizens = new NativeList<int>(INITIAL_HOUSEHOLD_CAPACITY, Allocator.Persistent),
                    State = SelectionSlotState.Free,
                };
            }
            m_StagingSlot = SELECTION_SLOT_NONE;
            m_PublishA = true;
            m_Accumulator = new NativeAccumulator<ResidentPopulationData>(Allocator.Persistent);
            m_EligibilityA = new NativeParallelHashSet<Entity>(INITIAL_HOUSEHOLD_CAPACITY, Allocator.Persistent);
            m_EligibilityB = new NativeParallelHashSet<Entity>(INITIAL_HOUSEHOLD_CAPACITY, Allocator.Persistent);
            m_LiveCounts = new NativeParallelHashMap<Entity, int>(INITIAL_HOUSEHOLD_CAPACITY, Allocator.Persistent);

#pragma warning disable CIVIC459 // PublishSnapshots bumps the explicit initial derived snapshot versions.
            PublishSnapshots(default, SELECTION_SLOT_NONE);
#pragma warning restore CIVIC459
            m_WasReset = true;
            // OnCreate publishes empty placeholder snapshots but no city data exists yet:
            // readiness stays NotReady (A1). Log the baseline for the [POP-READY] contour.
            SetReadiness(ResidentPopulationReadiness.NotReady, "create");

            ServiceRegistry.Instance.Register<IResidentHouseholdView>(this);
            ServiceRegistry.Instance.Register<IResidentPopulationEligibilityView>(this);
            // Registered in OnCreate like the views, which is what lets a consumer resolve it
            // lazily from ValidateAfterLoad (post-load validation precedes OnStartRunning).
            ServiceRegistry.Instance.Register<ICityPopulationReader>(this);
            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.StateChange);

            Log.Info("Created - registered resident population model");
        }

        [CompletesDependency("ResidentPopulationModel steady-state: complete previous chunk+flatten chain, publish its selection/eligibility result by rotating the selection ring (no copy), then schedule the next chain. The only [CompletesDependency] left in this system — ValidateAfterLoad no longer blocks. No hot-path entity-count sync; the retired main-thread selection copy cost 4+ ms per throttled tick on a large city while both Complete() waits measured zero.")]
        protected override void OnThrottledUpdate()
        {
            PublishCompletedScheduledResult();
            PrepareBackBuffers();
            if (!SchedulePopulationJob())
                PublishEmptySnapshotIfNeeded();
        }

        public ViewSnapshot<ResidentHouseholdSnapshot> Observe(ref int observerVersion)
        {
            bool changed = observerVersion != m_HouseholdVersion;
            if (changed)
                observerVersion = m_HouseholdVersion;

            return new ViewSnapshot<ResidentHouseholdSnapshot>(m_HouseholdSnapshot, m_HouseholdVersion, changed);
        }

        public void AckPendingDays(int dayCount)
        {
            if (dayCount <= 0)
                return;

            if (dayCount > m_PendingDayChanges)
                throw new ArgumentOutOfRangeException(nameof(dayCount), dayCount, "Cannot acknowledge more pending day changes than the current resident-household snapshot has published.");

            m_PendingDayChanges -= dayCount;
#pragma warning disable CIVIC459 // Ack publishes a new view cursor over the same model result and updated pending-day count.
            RepublishHouseholdSnapshotSamePair();
#pragma warning restore CIVIC459
        }

        public JobHandle GetReadJobHandle()
        {
            // Published eligibility buffers are swapped only after m_ModelWriteDependencies
            // completes; no writer targets the front buffer after publication.
            return default;
        }

        public void AddEligibilityReader(JobHandle reader)
        {
            m_EligibilityReadDependencies = JobHandle.CombineDependencies(m_EligibilityReadDependencies, reader);
        }

        // ORDER-INVARIANT: CrisisMonitorSystem and other DEFAULT readers resolve this view
        // in the same post-load pass, so the model's rebuild must be scheduled before
        // HydrationPriority.DEFAULT.
        public int HydrationOrder => HydrationPriority.POPULATION_SEED;

        public void ValidateAfterLoad()
        {
            // Mark the load rebuild so [POP-READY] publish-site logs attribute the
            // transition to source=seed rather than steady-state rebuild/empty.
            m_InLoadSeed = true;
            try
            {
                // One route, unconditionally. The selection is the only thing this system
                // publishes and it is never persisted, so there is nothing a load can
                // restore and nothing to branch on: the rebuild is always scheduled and
                // always asynchronous. Its three consumers (Exodus, WellbeingResolver,
                // CrisisMonitor) gate on IsSelectionReady and retry, so a tick that lands
                // before the job completes is skipped, not decided on cold data.
                ScheduleSelectionRebuildAfterLoad();
            }
            finally
            {
                m_InLoadSeed = false;
            }
        }

        // Post-load rebuild (A2): schedule the selection rebuild asynchronously —
        // SelectionReady is set when the chunk job completes on a later throttle. No
        // blocking Complete(): every selection consumer is gated and throttled, and
        // tolerates the async gap.
        private void ScheduleSelectionRebuildAfterLoad()
        {
            // Drop any in-flight handle from the previous city's instance without blocking
            // (it was already dropped by ResetTransientLoadState in Deserialize; this is the
            // defensive boot baseline for instance reuse). The dropped fire's staging slot
            // goes back to the ring, or it would leak as Staging forever (ring exhaustion
            // after three loads). Safe without Complete: the flatten job is registered in
            // Dependency, so the engine's load purge already finished it.
            // Clear the native scratch so the rebuild starts from an empty selection.
            m_HasScheduledResult = false;
            ReleaseStagingSlot();
            m_Accumulator.Clear();
            PrepareBackBuffers();

            // Post-load rebuild: ResizeBackContainersJob in the dependency chain sizes
            // ParallelWriter targets from the current query (vanilla
            // GeometrySystem.AllocateBuffersJob pattern, capacity fix kept), so the first
            // rebuild job sees correct capacity without a managed sync point.
            if (!SchedulePopulationJob())
            {
                // Empty city is a fully ready state, not a withheld one (A4): selection IS
                // ready, simply empty. PublishEmptySnapshotIfNeeded advances readiness to
                // SelectionReady directly (no job completes for an empty query), so an
                // empty city does not stay pinned at NotReady forever.
#pragma warning disable CIVIC459 // PublishEmptySnapshotIfNeeded calls PublishSnapshots, which bumps the household version cursor.
                PublishEmptySnapshotIfNeeded();
#pragma warning restore CIVIC459
            }

            Log.Info($"ValidateAfterLoad scheduled async selection rebuild: pendingDays={m_PendingDayChanges}, readiness={m_Readiness}");
        }

        private void OnDayChanged(DayChangedEvent evt)
        {
            // The model is the single owner of the pending-day count (A7): it accumulates
            // here on DayChanged and drains it through AckPendingDays. Consumers read the
            // published count via the snapshot rather than tracking their own counter.
            m_PendingDayChanges++;

            if (Log.IsDebugEnabled)
                Log.Info($"[POP-READY] Exodus day={evt.DayNumber} readiness={m_Readiness} pending={m_PendingDayChanges} action=accumulate");
        }

        private void PublishCompletedScheduledResult()
        {
            if (!m_HasScheduledResult)
                return;

            // The chain (count + flatten) was scheduled one throttle tick (~500 ms) ago
            // and is long finished on workers (measured at zero wait), so the Complete is
            // dependency bookkeeping, not a drain. The staging pair is fully written by
            // FlattenSelectionJob once this returns.
            m_ModelWriteDependencies.Complete();
            ResidentPopulationData completed = m_Accumulator.GetResult();
            SwapBuffers();
            int selectionSlot = m_StagingSlot;
            m_StagingSlot = SELECTION_SLOT_NONE;
#pragma warning disable CIVIC459 // PublishSnapshots bumps both cursors for the last-completed model result.
            PublishSnapshots(completed, selectionSlot);
#pragma warning restore CIVIC459
            m_Accumulator.Clear();
            m_HasScheduledResult = false;
            // A completed chunk job filled the selection and its alive-citizen sum in one
            // pass, so the published result is valid as a whole: SelectionReady (A1).
            // Always source=rebuild: the post-load route only SCHEDULES the job, so
            // m_InLoadSeed is back to false (reset in ValidateAfterLoad's finally) long
            // before any completion reaches this line.
            SetReadiness(ResidentPopulationReadiness.SelectionReady, "rebuild");
        }

        private void PrepareBackBuffers()
        {
            // Completes the handles consumer systems registered via AddEligibilityReader;
            // measured at zero wait — consumer read jobs never survive into our
            // throttle tick.
            m_EligibilityReadDependencies.Complete();
            ref NativeParallelHashSet<Entity> eligibility = ref GetCurrentBackEligibility();
            eligibility.Clear();
            // Single live-counts map: safe to clear here — its writer/reader jobs from the
            // previous fire are inside m_ModelWriteDependencies, completed by
            // PublishCompletedScheduledResult above (or never scheduled).
            m_LiveCounts.Clear();
        }

        private bool SchedulePopulationJob()
        {
            if (m_HouseholdQuery.IsEmptyIgnoreFilter)
                return false;

            // The ring guarantees a Free slot here in every legal sequence (rotation
            // returns the old previous-slot before scheduling; boot/reset states start
            // with extra Free slots). A miss is a ring-accounting bug: log loudly and
            // skip this fire — m_HasScheduledResult stays false, the published snapshot
            // stays intact, and the next tick retries. Returning true bypasses the
            // empty-publish fallback (an empty publish over a live city would be worse).
            int stagingSlot = AcquireFreeSelectionSlot();
            if (stagingSlot == SELECTION_SLOT_NONE)
            {
                Log.Error($"Selection ring has no Free slot at schedule time (states: {m_SelectionSlots[0].State}/{m_SelectionSlots[1].State}/{m_SelectionSlots[2].State}) — skipping this rebuild fire.");
                return true;
            }
            m_StagingSlot = stagingSlot;

            m_EntityType.Update(this);
            m_HouseholdCitizenType.Update(this);
            m_CitizenLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_HealthProblemLookup.Update(this);

            // Vanilla pattern (Game.Net.GeometrySystem.AllocateBuffersJob, decompile-verified):
            // 1) ToEntityListAsync fills a temp NativeList with current matching entities;
            // 2) ResizeBackContainersJob (IJob) reads list.Length and sizes ParallelWriter
            //    targets to actual*2 inside the dep chain;
            // 3) IJobChunk parallel writers run AFTER the resize, capacity guaranteed to
            //    cover job-time entity count even when households grow between fires.
            JobHandle listHandle;
            NativeList<Entity> householdList = m_HouseholdQuery.ToEntityListAsync(
                World.UpdateAllocator.ToAllocator,
                out listHandle);
            ref NativeParallelHashSet<Entity> backEligibility = ref GetCurrentBackEligibility();

            var resizeJob = new ResizeBackContainersJob
            {
                Entities = householdList.AsDeferredJobArray(),
                Eligibility = backEligibility,
                LiveCounts = m_LiveCounts,
            };
            JobHandle resizeInput = JobHandle.CombineDependencies(Dependency, listHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ResizeBackContainersJob.Schedule queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} householdList={householdList.IsCreated} eligibility={backEligibility.IsCreated}/capacity={backEligibility.Capacity} liveCounts={m_LiveCounts.IsCreated}/capacity={m_LiveCounts.Capacity}");
            JobHandle resizeHandle = resizeJob.Schedule(resizeInput);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ResizeBackContainersJob.Schedule queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} householdList={householdList.IsCreated} eligibility={backEligibility.IsCreated}/capacity={backEligibility.Capacity} liveCounts={m_LiveCounts.IsCreated}/capacity={m_LiveCounts.Capacity}");

            var job = new CountResidentPopulationJob
            {
                EntityType = m_EntityType,
                HouseholdCitizenType = m_HouseholdCitizenType,
                CitizenLookup = m_CitizenLookup,
                DeletedLookup = m_DeletedLookup,
                HealthProblemLookup = m_HealthProblemLookup,
                EligibleHouseholds = GetCurrentBackEligibility().AsParallelWriter(),
                LiveCounts = m_LiveCounts.AsParallelWriter(),
                PopulationData = m_Accumulator.AsParallelWriter()
            };

            JobHandle countInput = JobHandle.CombineDependencies(resizeHandle, m_EligibilityReadDependencies);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CountResidentPopulationJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} eligibility={backEligibility.IsCreated}/capacity={backEligibility.Capacity} liveCounts={m_LiveCounts.IsCreated}/capacity={m_LiveCounts.Capacity} accumulator={m_Accumulator.IsCreated}");
            Dependency = job.ScheduleParallel(
                m_HouseholdQuery,
                countInput);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post CountResidentPopulationJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} eligibility={backEligibility.IsCreated}/capacity={backEligibility.Capacity} liveCounts={m_LiveCounts.IsCreated}/capacity={m_LiveCounts.Capacity} accumulator={m_Accumulator.IsCreated}");

            // Flatten the selection inside the job graph — replaces the retired
            // main-thread copy (GetKeyValueArrays + per-element list adds, measured at
            // 4.28 ms/tick). MUST be scheduled on Dependency, not on
            // m_ModelWriteDependencies alone: a bare IJob over ring lists carries no ECS
            // handles, and only Dependency-registered handles are completed by the
            // engine's load purge — otherwise an in-game load could leave an orphaned
            // flatten writing into a slot the reset path already handed out.
            ref SelectionSlot staging = ref m_SelectionSlots[stagingSlot];
            var flattenJob = new FlattenSelectionJob
            {
                LiveCounts = m_LiveCounts,
                Households = staging.Households,
                LiveCitizens = staging.LiveCitizens,
            };
            Dependency = flattenJob.Schedule(Dependency);

            m_EligibilityReadDependencies = default;
            m_ModelWriteDependencies = Dependency;
            m_HasScheduledResult = true;
            m_WasReset = false;
            return true;
        }

        private void PublishSnapshots(ResidentPopulationData data, int selectionSlot)
        {
            // Stamp the alive-citizen sum onto the slot BEFORE the household snapshot is
            // built over it: the count job that produced `data` is the same job that filled
            // this slot's pair (PublishCompletedScheduledResult takes both from one completed
            // chain), so the sum and the per-household breakdown under it are one measurement.
            // Every rotation publish passes through here; the ack re-publish then re-reads the
            // stamp from the slot, which is why a catch-up day two never sees a zero.
            if (selectionSlot != SELECTION_SLOT_NONE)
                m_SelectionSlots[selectionSlot].AliveCitizens = data.AliveResidentCitizens;
#pragma warning disable CIVIC459 // Bump sits one call down (PublishHouseholdSnapshotRotating advances m_HouseholdVersion); the rule only searches this method body.
            PublishHouseholdSnapshotRotating(selectionSlot);
#pragma warning restore CIVIC459
        }

        /// <summary>
        /// Rotation publish: the previous snapshot's slot returns to the ring
        /// (identity-checked), the current snapshot becomes previous, and a new snapshot
        /// borrows <paramref name="selectionSlot"/> (the staging slot filled by
        /// FlattenSelectionJob, or SELECTION_SLOT_NONE for boot/empty/reset
        /// publishes, which borrow nothing). The ONLY path that rotates the ring.
        /// </summary>
        private void PublishHouseholdSnapshotRotating(int selectionSlot)
        {
            m_HouseholdVersion = NextVersion(m_HouseholdVersion);
#pragma warning disable CIVIC458 // Household cursor is intentionally independent from the scalar population cursor.
            ReturnBorrowedSlot(in m_PreviousHouseholdSnapshot);
            m_PreviousHouseholdSnapshot = m_HouseholdSnapshot;
            m_HouseholdSnapshot = CreateSnapshotOverSlot(selectionSlot);
#pragma warning restore CIVIC458
        }

        /// <summary>
        /// Re-publish of the SAME selection with a new version/pending-day count
        /// (AckPendingDays — Exodus calls it several times in a row between producer
        /// ticks). Deliberately a separate path from the rotation publish: it must not
        /// touch the previous snapshot or rotate the ring — shared code with the
        /// rotation path would alias current/previous onto one pair and double-return
        /// it, letting the next flatten write into the published selection.
        /// </summary>
        private void RepublishHouseholdSnapshotSamePair()
        {
            m_HouseholdVersion = NextVersion(m_HouseholdVersion);
#pragma warning disable CIVIC458 // Ack re-publish advances only the household cursor over the same model result.
            m_HouseholdSnapshot = CreateSnapshotOverSlot(m_HouseholdSnapshot.SelectionSlot);
#pragma warning restore CIVIC458
        }

        /// <summary>
        /// Builds the published snapshot over a ring slot, marking the slot Borrowed with
        /// the new version as the identity token (also on re-publish, so the eventual
        /// rotation return matches the LATEST borrower version). Slot NONE builds an
        /// empty snapshot that borrows nothing.
        /// </summary>
        private ResidentHouseholdSnapshot CreateSnapshotOverSlot(int selectionSlot)
        {
            if (selectionSlot == SELECTION_SLOT_NONE)
            {
                // Borrows nothing, so it sums nothing: zero here is not a reading of an
                // empty city, it is the absence of a selection to read. Consumers gate on
                // IsSelectionReady before dividing by anything from this snapshot.
                return new ResidentHouseholdSnapshot(
                    m_HouseholdVersion, default, default, 0, m_PendingDayChanges,
                    CatchUpPolicy.EachDay, SELECTION_SLOT_NONE);
            }

            ref SelectionSlot slot = ref m_SelectionSlots[selectionSlot];
            slot.State = SelectionSlotState.Borrowed;
            slot.BorrowVersion = m_HouseholdVersion;
            return new ResidentHouseholdSnapshot(
                m_HouseholdVersion,
                slot.Households.AsArray().AsReadOnly(),
                slot.LiveCitizens.AsArray().AsReadOnly(),
                // Same slot, same stamp: the re-publish path lands here too, so an acked
                // catch-up day gets the sum that matches the pair it is dividing by.
                slot.AliveCitizens,
                m_PendingDayChanges,
                CatchUpPolicy.EachDay,
                selectionSlot);
        }

        /// <summary>
        /// Identity-checked return: the slot goes Free only when it is currently
        /// Borrowed by EXACTLY this snapshot (version match). A double return, a return
        /// of a re-published slot's stale predecessor, or a foreign snapshot copy is a
        /// structural no-op — ownership protection by construction, not convention.
        /// </summary>
        private void ReturnBorrowedSlot(in ResidentHouseholdSnapshot snapshot)
        {
            int index = snapshot.SelectionSlot;
            if (index == SELECTION_SLOT_NONE)
                return;

            ref SelectionSlot slot = ref m_SelectionSlots[index];
            if (slot.State != SelectionSlotState.Borrowed || slot.BorrowVersion != snapshot.Version)
                return;

            slot.State = SelectionSlotState.Free;
        }

        private int AcquireFreeSelectionSlot()
        {
            for (int i = 0; i < m_SelectionSlots.Length; i++)
            {
                if (m_SelectionSlots[i].State != SelectionSlotState.Free)
                    continue;
                m_SelectionSlots[i].State = SelectionSlotState.Staging;
                return i;
            }

            return SELECTION_SLOT_NONE;
        }

        /// <summary>
        /// Hands the in-flight staging slot back to the ring when a scheduled fire is
        /// dropped without publishing (load paths). Field-only — never completes a
        /// handle; callers guarantee the flatten job is finished (blocking seed) or was
        /// finished by the engine's load purge (its handle is Dependency-registered).
        /// </summary>
        private void ReleaseStagingSlot()
        {
            if (m_StagingSlot == SELECTION_SLOT_NONE)
                return;

            ref SelectionSlot slot = ref m_SelectionSlots[m_StagingSlot];
            if (slot.State == SelectionSlotState.Staging)
                slot.State = SelectionSlotState.Free;
            m_StagingSlot = SELECTION_SLOT_NONE;
        }

        private void PublishEmptySnapshotIfNeeded()
        {
            // An empty household query is a fully ready state, not a withheld one (A4):
            // selection IS ready, it is simply empty. Set readiness unconditionally so
            // an empty city reaches SelectionReady even when the m_WasReset gate below
            // suppresses re-publishing the already-zero snapshot — otherwise consumer
            // gates would freeze an empty city forever. Snapshot data stays untouched
            // (already empty); only the readiness latch advances on the suppressed path.
            SetReadiness(ResidentPopulationReadiness.SelectionReady, m_InLoadSeed ? "seed" : "empty");

            if (m_WasReset)
                return;

            // Swap presents the cleared back ELIGIBILITY set as published; the household
            // selection publishes over SELECTION_SLOT_NONE (an empty snapshot borrows
            // nothing from the ring — strictly safer than burning a Free pair on zeros).
            SwapBuffers();
#pragma warning disable CIVIC459 // Empty-query transition publishes one empty snapshot instead of freezing the last non-empty selection.
            PublishSnapshots(default, SELECTION_SLOT_NONE);
#pragma warning restore CIVIC459
            m_WasReset = true;
        }

        // Single mutation point for the readiness source-of-truth (A1). Every change
        // (and the create/reset baseline) flows through here so the [POP-READY] contour
        // captures from->to, selectedSpeed, the published selection size and its
        // alive-citizen sum.
        //
        // Pause (Axiom 14): readiness is NOT set in pause on the load path any more. It
        // used to be, because Deserialize (pause-safe) restored a scalar and latched
        // ScalarReady there; with the scalar gone the only load-time caller is
        // ValidateAfterLoad, which runs inside PostLoadValidationSystem in GameSimulation
        // (SystemRegistrar.cs) and does not tick while paused. SelectionReady therefore
        // arrives on the first unpaused throttle tick — which is what every consumer's
        // IsSelectionReady gate is for.
        private void SetReadiness(ResidentPopulationReadiness to, string source)
        {
            ResidentPopulationReadiness from = m_Readiness;
            m_Readiness = to;

            if (!Log.IsDebugEnabled)
                return;

            float selectedSpeed = m_SimulationSystem != null ? m_SimulationSystem.selectedSpeed : -1f;
            // Asked through the slot, not off the array: this runs from the create/reset
            // baselines too, where the published snapshot borrows nothing and its array
            // handles are default. A snapshot that borrows nothing has no size to report.
            int eligibleCount = m_HouseholdSnapshot.SelectionSlot == SELECTION_SLOT_NONE
                ? 0
                : m_HouseholdSnapshot.EligibleHouseholds.Length;
            Log.Info($"[POP-READY] from={from} to={to} selectedSpeed={selectedSpeed} pop={m_HouseholdSnapshot.AliveCitizensInSelection} eligibleCount={eligibleCount} source={source}");
        }

        // Flips only the ELIGIBILITY hashset double-buffer (consumer jobs read the
        // published side via AddEligibilityReader). The household selection no longer
        // double-buffers here — it lives in the identity-checked ring; the single
        // live-counts map needs no parity at all (writer and reader are in one
        // dependency chain).
        private void SwapBuffers()
        {
            m_PublishA = !m_PublishA;
        }

        private ref NativeParallelHashSet<Entity> GetCurrentPublishedEligibility()
        {
            if (m_PublishA)
                return ref m_EligibilityA;
            return ref m_EligibilityB;
        }

        private ref NativeParallelHashSet<Entity> GetCurrentBackEligibility()
        {
            if (m_PublishA)
                return ref m_EligibilityB;
            return ref m_EligibilityA;
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IResidentHouseholdView>(this);
                ServiceRegistry.Instance.Unregister<IResidentPopulationEligibilityView>(this);
                ServiceRegistry.Instance.Unregister<ICityPopulationReader>(this);
            }

            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);
            m_ModelWriteDependencies.Complete();
            m_EligibilityReadDependencies.Complete();
            if (m_HouseholdQuery != default) m_HouseholdQuery.Dispose();
            if (m_Accumulator.IsCreated) m_Accumulator.Dispose();
            if (m_EligibilityA.IsCreated) m_EligibilityA.Dispose();
            if (m_EligibilityB.IsCreated) m_EligibilityB.Dispose();
            if (m_LiveCounts.IsCreated) m_LiveCounts.Dispose();
            // The ring is the permanent owner of the selection pairs — snapshots only
            // borrow, so disposal happens here and only here (CIVIC236/023).
            for (int i = 0; i < m_SelectionSlots.Length; i++)
            {
                if (m_SelectionSlots[i].Households.IsCreated)
                    m_SelectionSlots[i].Households.Dispose();
                if (m_SelectionSlots[i].LiveCitizens.IsCreated)
                    m_SelectionSlots[i].LiveCitizens.Dispose();
            }
            base.OnDestroy();
        }

        private static int NextVersion(int version) => version == int.MaxValue ? 1 : version + 1;

        private struct ResidentPopulationData : IAccumulable<ResidentPopulationData>
        {
            /// <summary>
            /// The one number this accumulator still carries: the sum of live citizens over
            /// the households the same job pass put into the selection. It is stamped onto
            /// the published selection slot (<see cref="PublishSnapshots"/>) and read from
            /// there as <c>ResidentHouseholdSnapshot.AliveCitizensInSelection</c>. The
            /// household-shape counters that used to sit beside it (eligible / homeless /
            /// moved-in) went out with the scalar snapshot that was their only reader.
            /// </summary>
            public int AliveResidentCitizens;

            public void Accumulate(ResidentPopulationData other)
            {
                AliveResidentCitizens += other.AliveResidentCitizens;
            }
        }

        #if ENABLE_BURST
        [BurstCompile]
        #endif
        private struct ResizeBackContainersJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            public NativeParallelHashSet<Entity> Eligibility;
            public NativeParallelHashMap<Entity, int> LiveCounts;

            public void Execute()
            {
                // PERF-LOCK: 2x headroom (count*2) for the parallel HashSet/HashMap writers.
                // NOT the capacity==count NativeParallelMultiHashMap precedent — these are
                // single-key NativeParallel containers and 2x leaves comfortable margin above
                // count for parallel inserts without rehash. Do NOT "unify" this with the
                // spatial-hash count+16*MaxJobThreadCount formula: for large cities count*2 is
                // strictly MORE headroom (200k vs ~101k at 100k households), so that change
                // would shrink the buffer, not harden it.
                long target = math.max(INITIAL_HOUSEHOLD_CAPACITY, (long)Entities.Length * 2);
                int targetInt = (int)math.min(target, int.MaxValue);
                if (Eligibility.Capacity < targetInt) Eligibility.Capacity = targetInt;
                if (LiveCounts.Capacity < targetInt) LiveCounts.Capacity = targetInt;
            }
        }

        /// <summary>
        /// Flattens the live-counts hashmap (filled by CountResidentPopulationJob earlier
        /// in the same dependency chain) into the staging ring slot's lists. Replaces the
        /// retired main-thread copy (GetKeyValueArrays(Temp) + per-element list adds —
        /// 4+ ms per throttled tick on a large city, the bulk of the system's cost).
        /// Stays an IJob: NativeList writers are banned in parallel jobs (CIVIC129), and
        /// the O(households) sequential Burst fill is far inside the 500 ms throttle
        /// window (the heavier count job already completes with zero residual wait).
        /// </summary>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        private struct FlattenSelectionJob : IJob
        {
            [ReadOnly] public NativeParallelHashMap<Entity, int> LiveCounts;
            public NativeList<Entity> Households;
            public NativeList<int> LiveCitizens;

            public void Execute()
            {
                // Clear-before-Add inside Execute (CIVIC187): the staging slot may carry
                // a stale selection from its previous borrow cycle.
                Households.Clear();
                LiveCitizens.Clear();

                int count = LiveCounts.Count();
                if (count <= 0)
                    return;

                if (Households.Capacity < count)
                    Households.Capacity = count;
                if (LiveCitizens.Capacity < count)
                    LiveCitizens.Capacity = count;

                foreach (var pair in LiveCounts)
                {
                    Households.AddNoResize(pair.Key);
                    LiveCitizens.AddNoResize(pair.Value);
                }
            }
        }

        #if ENABLE_BURST
        [BurstCompile]
        #endif
        private struct CountResidentPopulationJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public BufferTypeHandle<HouseholdCitizen> HouseholdCitizenType;
            [ReadOnly] public ComponentLookup<Citizen> CitizenLookup;
            [ReadOnly] public ComponentLookup<Deleted> DeletedLookup;
            [ReadOnly] public ComponentLookup<HealthProblem> HealthProblemLookup;
            public NativeParallelHashSet<Entity>.ParallelWriter EligibleHouseholds;
            public NativeParallelHashMap<Entity, int>.ParallelWriter LiveCounts;
            public NativeAccumulator<ResidentPopulationData>.ParallelWriter PopulationData;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ResidentPopulationData data = default;
                NativeArray<Entity> households = chunk.GetNativeArray(EntityType);
                BufferAccessor<HouseholdCitizen> householdCitizens = chunk.GetBufferAccessor(ref HouseholdCitizenType);

                for (int i = 0; i < householdCitizens.Length; i++)
                {
                    int liveCitizens = 0;
                    DynamicBuffer<HouseholdCitizen> citizens = householdCitizens[i];
                    for (int j = 0; j < citizens.Length; j++)
                    {
                        Entity citizen = citizens[j].m_Citizen;
                        if (!IsResidentCitizen(citizen))
                            continue;

                        liveCitizens++;
                        checked { data.AliveResidentCitizens++; }
                    }

                    if (liveCitizens <= 0)
                        continue;

                    Entity household = households[i];
                    EligibleHouseholds.Add(household);
                    LiveCounts.TryAdd(household, liveCitizens);
                }

                PopulationData.Accumulate(data);
            }

            // Vanilla guarantees a citizen appears in exactly one household's HouseholdCitizen
            // buffer, so no cross-household dedup is needed: 702 rebuilds under a 20-tower
            // demolition (−2514 citizens) logged zero duplicate Adds, so the former
            // m_CountedCitizens set was pure overhead and was removed.
            private bool IsResidentCitizen(Entity citizen)
            {
                if (citizen == Entity.Null)
                    return false;
                if (!CitizenLookup.HasComponent(citizen))
                    return false;
                if (DeletedLookup.HasComponent(citizen))
                    return false;
                if (CitizenUtils.IsDead(citizen, ref HealthProblemLookup))
                    return false;

                return true;
            }
        }
    }
}
