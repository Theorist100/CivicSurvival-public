using Game;
using Game.Areas;
using CivicSurvival.Core.Features.Wellbeing;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Jobs;
using CivicSurvival.Domains.Cognitive.Threats.Systems;
using CivicSurvival.Core.Components.Domain.NeighborEnvy;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Central resolver for all mental health effects.
    /// SINGLE WRITER to HouseholdPsyState - eliminates race conditions.
    ///
    /// Logic Composition Pattern:
    /// - Calls 6 calculators in sequence via ResolveHouseholdPsyJob
    /// - BlackoutCalculator: power status → pressure, hours
    /// - EnvyCalculator: neighbor power → pressure
    /// - ExposureCalculator: district/hero/telemarathon → exposure
    /// - ResistanceCalculator: education → resistance (throttled)
    /// - CognitiveCalculator: exposure → infection
    /// - TraumaCalculator: pressure → trauma, inertia
    ///
    /// Execution order: AFTER NeighborEnvySystem (needs EnvyAffected tags)
    ///
    /// Perf: Coverage + IPSO maps built on worker thread via BuildMentalHealthLookupsJob.
    /// ECS chains: WriteSingletonJob → BuildLookupsJob → ResolveHouseholdPsyJob (all workers).
    /// Main thread cost: ~0ms for map builds.
    /// </summary>
#pragma warning disable CIVIC076 // Runs every frame by design (resolver system, checks internally)
    [ActIndependent]
    public partial class MentalHealthResolverSystem : CivicSystemBase
#pragma warning restore CIVIC076
    {
        private static readonly LogContext Log = new("MentalHealthResolverSystem");

        // Simulation-tick based throttle (FPS-independent, like vanilla CitizenHappinessSystem)
        private const int SIM_TICK_INTERVAL = 16; // fire every 16 sim ticks
        private const int SLOT_COUNT = 4;         // 4 groups → full cycle = 64 ticks (~0.75s at 85 ticks/s)
        private const int SLOT_MASK = SLOT_COUNT - 1; // 0x3
        private const byte ALL_IMPACT_SLOT_MASK = (byte)((1 << SLOT_COUNT) - 1);
        private const byte MAX_EMPTY_RECIPIENT_DEFERRALS = 16;
        private const int DISTRICT_LOOKUP_CAPACITY = 10_000;
        private SimulationSystem m_SimulationSystem = null!;
        private uint m_LastSlot = uint.MaxValue; // force fire on first tick
        private float m_AccumulatedDt;
        // Lag spike protection: clamp accumulated dt to (interval_seconds × max_speed × safety)
        private const float MAX_ACCUMULATED_DT = (SIM_TICK_INTERVAL / 85f) * 3f * 2f;
#pragma warning disable CIVIC031, S2696 // Cross-system sync flag — read by PsyTransientResetSystem
        private static volatile bool s_DidFire;
        public static bool DidFire => s_DidFire;

        // PsySlot rotation: 4 slots, process one per fire (sim-tick driven)
        // CurrentSlot lives on PsySlot (Core) so Core systems can read without Domain dependency.
        /// <summary>Slot index processed on last fire frame. Delegates to PsySlot.CurrentSlot.</summary>
        public static int CurrentSlot => PsySlot.CurrentSlot;
#pragma warning restore CIVIC031, S2696

        private EntityQuery m_HouseholdQuery;
        private EntityQuery m_SpotterCmQuery;

        // Lazy init: create mod entities for new households (moved from PILS Phase 2)
        // Throttled to BULK_CREATE_SECONDS — new households can wait a few seconds for mental health init
        private EntityQuery m_UntaggedHouseholdQuery;
        private EntityQuery m_ModEntityQuery;
        // PERF: Skip BulkCreate during combat — EntityManager.CreateEntity costs 20-36ms on 177K entity world
        private EntityQuery m_ActiveThreatQuery;
        private EntityQuery m_BackupPowerStateQuery;
        private EntityArchetype m_ModArchetype;
#pragma warning disable CIVIC269 // Write via IJobParallelFor, not direct indexer
        private ComponentLookup<HouseholdPsyState> m_PsyStateLookup;
#pragma warning restore CIVIC269
#pragma warning disable CIVIC168 // Throttle timer, not persisted — starts from 0 on load (first fire after 5s)
        private float m_BulkCreateTimer;
        private float m_BulkCreateInterval = BULK_CREATE_IDLE_SECONDS;
#pragma warning restore CIVIC168
        // CreateEntity cost scales with entity count: 5ms@16 → 44ms@256.
        // Smaller batches = smaller spikes, more iterations to drain backlog.
        private const int BULK_CAP_SMALL = 8;
        private const int BULK_CAP_MEDIUM = 32;
        private const int BULK_CAP_LARGE = 64;
        private const float BULK_CREATE_IDLE_SECONDS = 5f;
        private const float BULK_CREATE_DRAIN_SECONDS = 1f;

        // Cached lookups (PERF: create in OnCreate, update in OnUpdate)
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;
        private ComponentLookup<ElectricityConsumer> m_ElectricityLookup;
        private ComponentLookup<CurrentDistrict> m_DistrictLookup;
        private ComponentLookup<EnvyAffected> m_EnvyAffectedLookup;
        private BufferLookup<HouseholdCitizen> m_HouseholdCitizenLookup;
        private ComponentLookup<Citizen> m_CitizenLookup;

        // PsySlot assignment counter for incremental bulk creates (wraps at SLOT_COUNT)
        // Not serialized — resets to 0 on load, produces even distribution regardless of start
        [System.NonSerialized] private int m_SlotAssignCounter;

        private struct PendingImpactEntry
        {
            public ImpactDistrictEntry Impact;
            public byte PendingSlotMask;
            public byte EmptyRecipientDeferrals;
        }

        // Impact snapshots are slot-addressed: every impact is delivered exactly once to each PsySlot.
        // The pending list is main-thread only; each fired slot gets an owned TempJob snapshot.
        [NonEntityIndex] private NativeList<PendingImpactEntry> m_PendingImpacts;

        // Impact pressure buffer (snapshot from ImpactPressureSingleton)
        private EntityQuery m_ImpactSingletonQuery;

        // Worker-thread buffer lookups (read-only — no main-thread sync point)
        private BufferLookup<DistrictBatteryCoverage> m_CoverageLookup;
        private BufferLookup<IPSODistrictExposureBuffer> m_IPSOExposureLookup;
        private BufferLookup<InternetDisabledBuffer> m_InternetLookup;

        // Persistent containers (reused via Clear() in BuildJob — no per-frame alloc/dispose)
        // Keys are district indices (stable spatial index)
        [NonEntityIndex] private NativeHashMap<int, DistrictBatteryCoverage> m_CoverageMap;
        [NonEntityIndex] private NativeHashMap<int, float> m_IpsoMap;
        [NonEntityIndex] private NativeParallelHashSet<int> m_InternetDisabledSet;

        protected override string ProfileName => "MentalHealth.OnUpdate";

        protected override void OnCreate()
        {
            base.OnCreate();

            // PsySlot MUST be in query definition — without it, AddSharedComponentFilter
            // silently matches 0 chunks and ScheduleParallel processes 0 entities.
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadWrite<HouseholdPsyState>(),
                ComponentType.ReadOnly<PsySlot>()
            );

            m_SpotterCmQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterCountermeasuresState>()
            );

            // Lazy init: untagged housed households (moved from PILS Phase 2)
            m_UntaggedHouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.Exclude<HasPsyState>(),
                ComponentType.Exclude<Deleted>()
            );
            m_ModEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<HouseholdPsyState>(),
                ComponentType.Exclude<Deleted>()
            );
            m_ActiveThreatQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActiveThreat>(),
                ComponentType.Exclude<Deleted>()
            );
            m_BackupPowerStateQuery = GetEntityQuery(ComponentType.ReadOnly<BackupPowerStateSingleton>());
            m_ModArchetype = EntityManager.CreateArchetype(typeof(HouseholdPsyState), typeof(PsySlot));
            m_PsyStateLookup = GetComponentLookup<HouseholdPsyState>(false);

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            // Cache lookups
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true);
            m_ElectricityLookup = GetComponentLookup<ElectricityConsumer>(true);
            m_DistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_EnvyAffectedLookup = GetComponentLookup<EnvyAffected>(true);
            m_HouseholdCitizenLookup = GetBufferLookup<HouseholdCitizen>(true);
            m_CitizenLookup = GetComponentLookup<Citizen>(true);

            // Read-only buffer lookups for worker-thread map building
            m_CoverageLookup = GetBufferLookup<DistrictBatteryCoverage>(true);
            m_IPSOExposureLookup = GetBufferLookup<IPSODistrictExposureBuffer>(true);
            m_InternetLookup = GetBufferLookup<InternetDisabledBuffer>(true);

            // Fixed high-water capacity avoids main-thread resize/sync before worker lookup builds.
            m_CoverageMap = new NativeHashMap<int, DistrictBatteryCoverage>(DISTRICT_LOOKUP_CAPACITY, Allocator.Persistent);
            m_IpsoMap = new NativeHashMap<int, float>(DISTRICT_LOOKUP_CAPACITY, Allocator.Persistent);
            m_PendingImpacts = new NativeList<PendingImpactEntry>(64, Allocator.Persistent);
            m_InternetDisabledSet = new NativeParallelHashSet<int>(DISTRICT_LOOKUP_CAPACITY, Allocator.Persistent);

            m_ImpactSingletonQuery = GetEntityQuery(ComponentType.ReadOnly<ImpactPressureSingleton>());

            // Pressure pipeline registration: MHR is consumer only (reads blackout/envy/impact for wellbeing)
            PressureRegistry.RegisterConsumer(PressureChannel.Blackout, nameof(MentalHealthResolverSystem));
            PressureRegistry.RegisterConsumer(PressureChannel.Envy, nameof(MentalHealthResolverSystem));
            PressureRegistry.RegisterConsumer(PressureChannel.Impact, nameof(MentalHealthResolverSystem));

            Log.Info("Created (Logic Composition pattern, all lookups on worker)");
        }

        private void DrainImpactProducerBuffer(bool discard)
        {
            if (!m_ImpactSingletonQuery.TryGetSingletonEntity<ImpactPressureSingleton>(out var impactEntity)
                || !EntityManager.HasBuffer<ImpactDistrictEntry>(impactEntity))
                return;

            var impactBuffer = SystemAPI.GetBuffer<ImpactDistrictEntry>(impactEntity);
            if (impactBuffer.Length == 0)
                return;

            if (!discard)
            {
                for (int i = 0; i < impactBuffer.Length; i++)
                {
                    m_PendingImpacts.Add(new PendingImpactEntry
                    {
                        Impact = impactBuffer[i],
                        PendingSlotMask = ALL_IMPACT_SLOT_MASK,
                        EmptyRecipientDeferrals = 0
                    });
                }
            }

            impactBuffer.Clear();
        }

        [CompletesDependency("ReconcileLoadedHouseholdTags: lazy-create pre-pass materialises restored HouseholdPsyState keys and untagged households to avoid duplicate mod-entities when MHR runs before PILS after load")]
        private void ReconcileLoadedHouseholdTags()
        {
            if (m_ModEntityQuery.IsEmpty || m_UntaggedHouseholdQuery.IsEmpty)
                return;

            var households = m_UntaggedHouseholdQuery.ToEntityArray(Allocator.Temp);
            var states = m_ModEntityQuery.ToComponentDataArray<HouseholdPsyState>(Allocator.Temp);
            var existingKeys = new NativeParallelHashSet<long>(states.Length, Allocator.Temp);
            var existingToTag = new NativeList<Entity>(households.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < states.Length; i++)
                {
                    if (HouseholdPsyIdentity.IsValidHouseholdKey(states[i].HouseholdIndex, states[i].HouseholdVersion))
                        existingKeys.Add(HouseholdPsyIdentity.MakeHouseholdKey(states[i].HouseholdIndex, states[i].HouseholdVersion));
                }

                for (int i = 0; i < households.Length; i++)
                {
                    var household = households[i];
                    if (HouseholdPsyIdentity.IsValidHouseholdKey(household.Index, household.Version)
                        && existingKeys.Contains(HouseholdPsyIdentity.MakeHouseholdKey(household.Index, household.Version)))
                        existingToTag.Add(household);
                }

                if (existingToTag.Length > 0)
                {
#pragma warning disable CIVIC006 // Load-time bulk re-tag mirrors PILS reconcile; prevents duplicate lazy-create.
                    EntityManager.AddComponent<HasPsyState>(existingToTag.AsArray());
#pragma warning restore CIVIC006
                    Log.Info($"[LAZY INIT] Re-tagged {existingToTag.Length} restored PsyState households before create");
                }
            }
            finally
            {
                if (existingToTag.IsCreated) existingToTag.Dispose();
                if (existingKeys.IsCreated) existingKeys.Dispose();
                if (states.IsCreated) states.Dispose();
                if (households.IsCreated) households.Dispose();
            }
        }

        private NativeArray<ImpactDistrictEntry> BuildImpactSnapshotForSlot(int slot, int recipientCount, Allocator allocator)
        {
            byte slotBit = (byte)(1 << slot);
            if (recipientCount <= 0)
            {
                ExpireImpactSnapshotForEmptySlot(slot, slotBit);
                return default;
            }

            int impactCount = 0;
            for (int i = 0; i < m_PendingImpacts.Length; i++)
                if ((m_PendingImpacts[i].PendingSlotMask & slotBit) != 0)
                    impactCount++;

            if (impactCount == 0)
                return default;

            var snapshot = new NativeArray<ImpactDistrictEntry>(impactCount, allocator, NativeArrayOptions.UninitializedMemory);
            int writeIndex = 0;
            for (int i = m_PendingImpacts.Length - 1; i >= 0; i--)
            {
                var pending = m_PendingImpacts[i];
                if ((pending.PendingSlotMask & slotBit) != 0)
                {
                    snapshot[writeIndex++] = pending.Impact;
                    pending.PendingSlotMask = (byte)(pending.PendingSlotMask & ~slotBit);
                }

                if (pending.PendingSlotMask == 0)
                    m_PendingImpacts.RemoveAtSwapBack(i);
                else
                    m_PendingImpacts[i] = pending;
            }

            return snapshot;
        }

        private void ExpireImpactSnapshotForEmptySlot(int slot, byte slotBit)
        {
            for (int i = m_PendingImpacts.Length - 1; i >= 0; i--)
            {
                var pending = m_PendingImpacts[i];
                if ((pending.PendingSlotMask & slotBit) == 0)
                    continue;

                pending.EmptyRecipientDeferrals++;
                if (pending.EmptyRecipientDeferrals >= MAX_EMPTY_RECIPIENT_DEFERRALS)
                {
                    pending.PendingSlotMask = (byte)(pending.PendingSlotMask & ~slotBit);
                    Log.Warn($"Dropped impact pressure delivery for empty PsySlot {slot} after {MAX_EMPTY_RECIPIENT_DEFERRALS} deferrals");
                }

                if (pending.PendingSlotMask == 0)
                    m_PendingImpacts.RemoveAtSwapBack(i);
                else
                    m_PendingImpacts[i] = pending;
            }
        }

        [CompletesDependency("MentalHealthResolverSystem.OnUpdateImpl: (a) throttled bulk-create path (timer-gated, ~64 entities cap) materialises NativeArray<Entity> for Burst IJobParallelFor — no SystemAPI.Query equivalent; (b) impact-recipient CalculateEntityCount runs only when PendingImpacts non-empty (rare), drives BuildImpactSnapshotForSlot capacity. Not a [HotPathSystem]; mid-frequency resolver, throttled by m_AccumulatedDt")]
        protected override void OnUpdateImpl()
        {
            // Accumulate delta every frame (before any early return)
            float frameDt = SystemAPI.Time.DeltaTime;
            m_AccumulatedDt += frameDt;
            m_BulkCreateTimer = System.Math.Min(m_BulkCreateTimer + frameDt, m_BulkCreateInterval * 2f);

            // Sim-tick based throttle (FPS-independent, like vanilla CitizenHappinessSystem)
            // GetUpdateFrameWithInterval: (frameIndex / interval) & (slotCount - 1)
            uint currentSlot = SimulationUtils.GetUpdateFrameWithInterval(
                m_SimulationSystem.frameIndex, (uint)SIM_TICK_INTERVAL, SLOT_COUNT);
#pragma warning disable S2696 // Cross-system sync flag
            if (currentSlot == m_LastSlot)
            {
                s_DidFire = false;
                return;
            }
            m_LastSlot = currentSlot;
            s_DidFire = true;
            PsySlot.CurrentSlot = (int)currentSlot;
            m_HouseholdQuery.ResetFilter();
            m_HouseholdQuery.AddSharedComponentFilter(new PsySlot { SlotIndex = (int)currentSlot });
#pragma warning restore S2696

            // One-time validation: all systems have registered by first fire frame
            PressureRegistry.Validate();

            bool noPsyStateAndNoHouseholds = m_HouseholdQuery.IsEmptyIgnoreFilter && m_UntaggedHouseholdQuery.IsEmptyIgnoreFilter;
            DrainImpactProducerBuffer(discard: noPsyStateAndNoHouseholds);

            if (m_HouseholdQuery.IsEmptyIgnoreFilter)
            {
                // FIX H3: Don't early-return if untagged households exist — bulk create needs to run
                // to break the deadlock: "need PsyState to create PsyState"
                if (m_UntaggedHouseholdQuery.IsEmptyIgnoreFilter)
                {
                    m_AccumulatedDt = 0f;
                    return;
                }
                // Fall through to bulk create path with accumulated dt
            }

            // FIX M6: Clamp accumulated dt before bulk create timer (lag spike protection)
            // Same MAX_ACCUMULATED_DT used by resolver on line 284
            if (m_AccumulatedDt > MAX_ACCUMULATED_DT)
                m_AccumulatedDt = MAX_ACCUMULATED_DT;

            // ════════════════════════════════════════════════════════════════
            // LAZY INIT: Create mod entities for new households (ex-PILS Phase 2)
            // Throttled + adaptive cap: 16/64/256 by backlog size
            // Structural changes BEFORE lookup updates (Trap 1: invalidates pointers)
            // IsEmpty = chunk metadata check, zero cost when no new households (99.9%)
            // ════════════════════════════════════════════════════════════════
            // FIX L6: m_BulkCreateTimer advances from frameDt above; m_AccumulatedDt is resolver-only.
            // PERF: Skip BulkCreate during active combat — EntityManager.CreateEntity = 20-36ms on 177K world.
            // New households can wait until threats are gone. Timer keeps accumulating so first post-combat
            // fire triggers immediately.
            bool hasCombat = !m_ActiveThreatQuery.IsEmpty;
            if (!hasCombat && m_BulkCreateTimer >= m_BulkCreateInterval && !m_UntaggedHouseholdQuery.IsEmpty)
            {
                m_BulkCreateTimer = 0f;
                using (PerformanceProfiler.Measure("SP:MH.BulkCreate"))
                {
                    ReconcileLoadedHouseholdTags();
                    if (m_UntaggedHouseholdQuery.IsEmpty)
                    {
                        m_BulkCreateInterval = BULK_CREATE_IDLE_SECONDS;
                    }

                    NativeArray<Entity> allHouseholds;
                    using (PerformanceProfiler.Measure("MH.BulkCreate.ToEntityArray"))
                    {
                        allHouseholds = m_UntaggedHouseholdQuery.ToEntityArray(Allocator.TempJob);
                    }
                    int totalNew = allHouseholds.Length;
                    int cap;
                    if (totalNew > 128) cap = BULK_CAP_LARGE;
                    else if (totalNew > 16) cap = BULK_CAP_MEDIUM;
                    else cap = BULK_CAP_SMALL;
                    int processCount = totalNew > cap ? cap : totalNew;

                    if (processCount > 0)
                    {
                        NativeArray<Entity> modEntities;
                        using (PerformanceProfiler.Measure("MH.BulkCreate.CreateEntity"))
                        {
#pragma warning disable CIVIC006 // Bulk create — ECB impossible, need NativeArray for Burst fill
                            modEntities = EntityManager.CreateEntity(m_ModArchetype, processCount, Allocator.TempJob);
#pragma warning restore CIVIC006
                        }

                        // Assign PsySlot round-robin (structural, BEFORE fill job)
                        // Skip slot 0 — default SharedComponent value, SetSharedComponent asserts on same-chunk move
                        for (int i = 0; i < processCount; i++)
                        {
                            int slot = (m_SlotAssignCounter + i) & SLOT_MASK;
                            if (slot != 0)
                                EntityManager.SetSharedComponent(modEntities[i], new PsySlot { SlotIndex = slot });
                        }
                        m_SlotAssignCounter = (m_SlotAssignCounter + processCount) & SLOT_MASK;

                        // Copy batch for tagging (fill job holds allHouseholds — avoid aliasing)
                        var batchToTag = new NativeArray<Entity>(processCount, Allocator.TempJob);
                        try
                        {
                            NativeArray<Entity>.Copy(allHouseholds, batchToTag, processCount);

                            // Fill HouseholdIndex/Version via Burst job — reads allHouseholds[0..processCount-1]
                            // Structural change below completes Dependency implicitly
#pragma warning disable CIVIC289 // Intentional: Update after EntityManager.CreateEntity (structural change invalidates lookup pointers)
                            m_PsyStateLookup.Update(this);
#pragma warning restore CIVIC289
                            var fillJob = new FillPsyModEntitiesJob
                            {
                                ModEntities = modEntities,
                                Households = allHouseholds,
                                PsyStateLookup = m_PsyStateLookup
                            };
                            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre FillPsyModEntitiesJob.Schedule processCount={processCount} modEntities={modEntities.IsCreated}/{modEntities.Length} households={allHouseholds.IsCreated}/{allHouseholds.Length} batchToTag={batchToTag.IsCreated}/{batchToTag.Length}");
                            Dependency = fillJob.Schedule(processCount, 64, Dependency);
                            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post FillPsyModEntitiesJob.Schedule processCount={processCount} modEntities={modEntities.IsCreated}/{modEntities.Length} households={allHouseholds.IsCreated}/{allHouseholds.Length} batchToTag={batchToTag.IsCreated}/{batchToTag.Length}");
                            if (modEntities.IsCreated) Dependency = modEntities.Dispose(Dependency);
                            if (allHouseholds.IsCreated) Dependency = allHouseholds.Dispose(Dependency);

                            // Tag only processed batch — structural change completes Dependency (fill + dispose)
                            using (PerformanceProfiler.Measure("MH.BulkCreate.AddTag"))
                            {
#pragma warning disable CIVIC006 // Bulk tag on vanilla entities — same pattern as PowerCapacityModifiers
                                EntityManager.AddComponent<HasPsyState>(batchToTag);
#pragma warning restore CIVIC006
                            }
                        }
                        finally
                        {
                            if (batchToTag.IsCreated) batchToTag.Dispose();
                        }

                        // Dynamic interval: drain when backlog exceeds one future batch
                        int backlog = totalNew - processCount;
                        m_BulkCreateInterval = backlog > BULK_CAP_SMALL ? BULK_CREATE_DRAIN_SECONDS : BULK_CREATE_IDLE_SECONDS;

                        Log.Info($"[LAZY INIT] Created {processCount} PsyState (cap: {cap}, backlog: {backlog})");
                    }
                    else
                    {
                        if (allHouseholds.IsCreated) allHouseholds.Dispose();
                    }
                }
            }

            // Update lookups (AFTER structural changes — Trap 1)
            using (PerformanceProfiler.Measure("SP:MH.LookupSync"))
            {
                m_PropertyRenterLookup.Update(this);
                m_ElectricityLookup.Update(this);
                m_DistrictLookup.Update(this);
                m_EnvyAffectedLookup.Update(this);
                m_HouseholdCitizenLookup.Update(this);
                m_CitizenLookup.Update(this);
            }

            // Worker-thread buffer lookups (read-only update — instant, no sync)
            m_CoverageLookup.Update(this);
            m_IPSOExposureLookup.Update(this);
            m_InternetLookup.Update(this);

            // Time (accumulated across skipped frames, clamped against lag spikes)
            float dt = m_AccumulatedDt > MAX_ACCUMULATED_DT ? MAX_ACCUMULATED_DT : m_AccumulatedDt;
            // FIX W2-H1: Use TotalGameHours for persisted Resistance_LastUpdateTime
            // ElapsedTime resets on load → persisted timestamp blocks recalc for minutes
            bool forceResistanceRecalculate = !GameTimeSystem.TryGetTotalGameSeconds(out var currentSeconds);
            float currentTime = forceResistanceRecalculate
                ? 0f
                : (float)currentSeconds;

            // Config
            var balance = BalanceConfig.Current;
            if (balance == null) return;
            float dtHours = GameRate.HoursDelta(dt);
            m_AccumulatedDt = 0f;
            var cwCfg = balance.Cognitive;

            // ════════════════════════════════════════════════════════════════
            // IMPACT PRESSURE: each impact is delivered once to each PsySlot.
            // Rapid impacts append to pending state instead of replacing the active snapshot.
            // ════════════════════════════════════════════════════════════════
            NativeArray<ImpactDistrictEntry> recentImpacts = default;
            if (m_PendingImpacts.Length > 0)
            {
                int impactRecipientCount = m_HouseholdQuery.CalculateEntityCount();
                recentImpacts = BuildImpactSnapshotForSlot((int)currentSlot, impactRecipientCount, Allocator.TempJob);
            }

            // Get singletons
            var backupPolicy = BackupPolicy.Reserve;
            var heroStatus = HeroStatus.Inactive;
            bool isGlobalBlackout = false;
            float infectionRate = cwCfg.InfectionRateBase;
            float recoveryRate = cwCfg.RecoveryRateBase;
            bool telemarathonActive = false;
            bool telemarathonInShock = false;
            float telemarathonTrust = 0f;
            float telemarathonEffectiveness = 0f;
            float telemarathonModeBonus = 1f;
            float alarmistStressRate = 0f;

            using (PerformanceProfiler.Measure("MentalHealth.Singletons"))
            {
#pragma warning disable CIVIC211 // Gameplay dependency: mental health affected by backup power policy
                backupPolicy = (m_BackupPowerStateQuery.TryGetSingleton<BackupPowerStateSingleton>(out var bp)
                        ? bp : BackupPowerStateSingleton.Default)
                    .Policy;
#pragma warning restore CIVIC211

                if (SystemAPI.TryGetSingleton<CognitiveState>(out var cwState))
                {
                    var hs = SystemAPI.TryGetSingleton<HeroDeploymentState>(out var heroState)
                        ? heroState : HeroDeploymentState.Default;
                    heroStatus = hs.HeroStatus;
                    infectionRate = CognitiveRates.EffectiveInfectionRate(cwState, hs);
                    recoveryRate = CognitiveRates.EffectiveRecoveryRate(cwState, hs);
                    // T4-7 FIX: Pass global Internet Blackout mode to ExposureCalculator
                    isGlobalBlackout = cwState.InternetMode == GlobalInternetMode.Blackout;
                }

                if (SystemAPI.TryGetSingleton<TelemarathonRuntimeState>(out var telemarathon))
                {
                    telemarathonActive = telemarathon.IsActive;
                    telemarathonInShock = telemarathon.IsInShock;
                    telemarathonTrust = telemarathon.Trust;
                    telemarathonEffectiveness = telemarathon.EffectivenessMult;

                    if (telemarathon.IsActive && !telemarathon.IsInShock)
                    {
                        telemarathonModeBonus = telemarathon.Mode switch
                        {
                            NarrativeMode.Realistic => cwCfg.ModeBonusRealistic,
                            NarrativeMode.Alarmist => cwCfg.ModeBonusAlarmist,
                            NarrativeMode.Soothing => cwCfg.ModeBonusSoothing,
                            _ => 1f
                        };
                        alarmistStressRate = telemarathon.StressRate * telemarathon.EffectivenessMult;
                    }
                }
            }

            // ════════════════════════════════════════════════════════════════
            // WORKER-THREAD MAP BUILDS (all 3 lookups on worker — zero main-thread sync)
            // ECS chains: WriteSingletonJob/WriteIPSOStateJob → BuildLookupsJob → ResolveJob
            // ════════════════════════════════════════════════════════════════

            // Check entity availability (read-only — no sync point)
            bool hasIpsoData = false;
            var ipsoEntity = Entity.Null;
            if (SystemAPI.TryGetSingletonEntity<IPSOState>(out var ipsoEntityOut))
            {
                ipsoEntity = ipsoEntityOut;
                if (SystemAPI.TryGetSingleton<IPSOState>(out var ipso) && ipso.IsActive)
                    hasIpsoData = true;
            }

            var coverageEntity = SystemAPI.TryGetSingletonEntity<BackupPowerStateSingleton>(out var coverageEntityOut)
                ? coverageEntityOut
                : Entity.Null;
            bool hasInternetEntity = m_SpotterCmQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var internetEntity);

            // Persistent containers (cleared inside BuildJob.Execute)
            bool hasInternetData = hasInternetEntity; // job will fill the set; if entity missing, set stays empty

            // Schedule BuildMentalHealthLookupsJob (IJob — runs on worker, waits for buffer writers)
            JobHandle buildHandle;
            using (PerformanceProfiler.Measure("MentalHealth.ScheduleBuild"))
            {
                var buildJob = new BuildMentalHealthLookupsJob
                {
                    CoverageLookup = m_CoverageLookup,
                    CoverageEntity = coverageEntity,
                    IPSOLookup = m_IPSOExposureLookup,
                    IPSOEntity = ipsoEntity,
                    InternetLookup = m_InternetLookup,
                    InternetEntity = internetEntity,
                    CoverageMap = m_CoverageMap,
                    IPSOExposureMap = m_IpsoMap,
                    InternetDisabledSet = m_InternetDisabledSet
                };
                if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BuildMentalHealthLookupsJob.Schedule coverageEntity={coverageEntity} ipsoEntity={ipsoEntity} internetEntity={internetEntity} hasInternetData={hasInternetData} hasIpsoData={hasIpsoData} coverageMap={m_CoverageMap.IsCreated}/capacity={m_CoverageMap.Capacity} ipsoMap={m_IpsoMap.IsCreated}/capacity={m_IpsoMap.Capacity} internetSet={m_InternetDisabledSet.IsCreated}/capacity={m_InternetDisabledSet.Capacity}");
                buildHandle = buildJob.Schedule(Dependency);
                if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BuildMentalHealthLookupsJob.Schedule coverageEntity={coverageEntity} ipsoEntity={ipsoEntity} internetEntity={internetEntity} hasInternetData={hasInternetData} hasIpsoData={hasIpsoData} coverageMap={m_CoverageMap.IsCreated}/capacity={m_CoverageMap.Capacity} ipsoMap={m_IpsoMap.IsCreated}/capacity={m_IpsoMap.Capacity} internetSet={m_InternetDisabledSet.IsCreated}/capacity={m_InternetDisabledSet.Capacity}");
                // FIX H41: Defensive — keep Dependency in sync with buildHandle.
                // Chain is buildHandle → resolveJob → Dependency, but if resolveJob
                // scheduling is skipped, buildHandle would be orphaned.
                Dependency = buildHandle;
            }

            var bpCfg = balance.BackupPower;

            // ════════════════════════════════════════════════════════════════
            // RESOLVE JOB (parallel, depends on buildJob)
            // ════════════════════════════════════════════════════════════════

            using (PerformanceProfiler.Measure("MentalHealth.Schedule"))
            {
            var resolveJob = new ResolveHouseholdPsyJob
            {
                // Lookups
                PropertyRenterLookup = m_PropertyRenterLookup,
                ElectricityLookup = m_ElectricityLookup,
                DistrictLookup = m_DistrictLookup,
                EnvyAffectedLookup = m_EnvyAffectedLookup,
                HouseholdCitizenLookup = m_HouseholdCitizenLookup,
                CitizenLookup = m_CitizenLookup,

                // Internet disabled
                InternetDisabledDistricts = m_InternetDisabledSet,
                HasInternetData = hasInternetData,

                // IPSO per-district exposure (built by worker job)
                IPSODistrictExposure = m_IpsoMap,
                HasIpsoData = hasIpsoData,

                // T4-7 FIX: Global Internet Blackout mode
                IsGlobalBlackout = isGlobalBlackout,

                // Singletons
                BackupPolicy = backupPolicy,
                HeroStatus = heroStatus,

                // Three-layer battery coverage (built by worker job)
                DistrictCoverageMap = m_CoverageMap,
                MitigationWeightHospital = bpCfg.MitigationWeightHospital,
                MitigationWeightSchool = bpCfg.MitigationWeightSchool,
                MitigationWeightPrivate = bpCfg.MitigationWeightPrivate,
                MitigationMin = bpCfg.MitigationMin,

                // Telemarathon
                TelemarathonActive = telemarathonActive,
                TelemarathonInShock = telemarathonInShock,
                TelemarathonTrust = telemarathonTrust,
                TelemarathonEffectiveness = telemarathonEffectiveness,
                TelemarathonModeBonus = telemarathonModeBonus,
                AlarmistStressRate = alarmistStressRate,

                // Cognitive config
                EnemyInternetWeight = cwCfg.EnemyInternetWeight,
                EnemyIpsoWeight = cwCfg.EnemyIpsoWeight,
                CounterOpsMultiplier = cwCfg.CounterOpsMultiplier,
                SkepticismFactor = cwCfg.SkepticismFactor,
                InfectionRate = infectionRate,
                RecoveryRate = recoveryRate,
                BlackoutVulnThreshold = cwCfg.BlackoutVulnThresholdHours,
                BlackoutVulnMaxHours = cwCfg.BlackoutVulnMaxHours,
                BlackoutVulnMaxBonus = cwCfg.BlackoutVulnMaxBonus,

                // Trauma config
                EnvyStress = cwCfg.EnvyStress,
                TraumaGainRate = cwCfg.TraumaGainRate,
                TraumaDecayRate = cwCfg.TraumaDecayRate,
                InertiaGainRate = cwCfg.InertiaGainRate,
                InertiaDecayRate = cwCfg.InertiaDecayRate,

                // Impact pressure
                RecentImpacts = recentImpacts,
                ImpactDistantFactor = cwCfg.ImpactDistantFactor,

                // Time
                DeltaTime = dt,
                DeltaHours = dtHours,
                CurrentTime = currentTime,
                ForceResistanceRecalculate = forceResistanceRecalculate
            };

            // Chain: buildJob → resolveJob (parallel)
            // CIVIC184 suppressed: Dependency writes in BulkCreate block (fill + dispose) are completed
            // by AddComponent structural change before this point. No lost handles.
#pragma warning disable CIVIC184
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ResolveHouseholdPsyJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} recentImpacts={recentImpacts.IsCreated}/{recentImpacts.Length} hasInternetData={hasInternetData} hasIpsoData={hasIpsoData} coverageMap={m_CoverageMap.IsCreated}/capacity={m_CoverageMap.Capacity} ipsoMap={m_IpsoMap.IsCreated}/capacity={m_IpsoMap.Capacity} internetSet={m_InternetDisabledSet.IsCreated}/capacity={m_InternetDisabledSet.Capacity}");
            Dependency = resolveJob.ScheduleParallel(m_HouseholdQuery, buildHandle);
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ResolveHouseholdPsyJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} recentImpacts={recentImpacts.IsCreated}/{recentImpacts.Length} hasInternetData={hasInternetData} hasIpsoData={hasIpsoData} coverageMap={m_CoverageMap.IsCreated}/capacity={m_CoverageMap.Capacity} ipsoMap={m_IpsoMap.IsCreated}/capacity={m_IpsoMap.Capacity} internetSet={m_InternetDisabledSet.IsCreated}/capacity={m_InternetDisabledSet.Capacity}");
            if (recentImpacts.IsCreated)
                Dependency = recentImpacts.Dispose(Dependency);
#pragma warning restore CIVIC184

            // NOTE: Transient reset moved to PsyTransientResetSystem
            // (runs AFTER WellbeingResolverSystem reads the values)

            // No Dispose — containers are persistent (cleared in BuildJob each frame)
            } // MentalHealth.Schedule
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            if (m_CoverageMap.IsCreated) m_CoverageMap.Dispose();
            if (m_IpsoMap.IsCreated) m_IpsoMap.Dispose();
            if (m_InternetDisabledSet.IsCreated) m_InternetDisabledSet.Dispose();
            if (m_PendingImpacts.IsCreated) m_PendingImpacts.Dispose();

            // Symmetric deregister (matches OnCreate consumer registrations)
            PressureRegistry.DeregisterConsumer(PressureChannel.Blackout, nameof(MentalHealthResolverSystem));
            PressureRegistry.DeregisterConsumer(PressureChannel.Envy, nameof(MentalHealthResolverSystem));
            PressureRegistry.DeregisterConsumer(PressureChannel.Impact, nameof(MentalHealthResolverSystem));

            base.OnDestroy();
        }
    }
}
