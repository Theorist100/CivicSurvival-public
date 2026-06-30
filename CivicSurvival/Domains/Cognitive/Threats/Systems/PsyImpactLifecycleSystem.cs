using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Base;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    internal static class HouseholdPsyIdentity
    {
        public static bool IsValidHouseholdKey(int index, int version)
            => index >= 0 && version != 0;

        public static long MakeHouseholdKey(int index, int version)
            => ((long)index << 32) ^ (uint)version;
    }

    /// <summary>
    /// Burst job to fill HouseholdIndex/Version + DistrictLink_Index on freshly created mod entities.
    /// Uses IJobParallelFor with two NativeArrays (mod entities + households).
    /// [NativeDisableParallelForRestriction] safe: each ModEntities[i] is unique.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public struct FillPsyModEntitiesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> ModEntities;
        [ReadOnly] public NativeArray<Entity> Households;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<HouseholdPsyState> PsyStateLookup;

        public void Execute(int index)
        {
            // ModEntities are freshly created by us — guaranteed to have HouseholdPsyState
#pragma warning disable CIVIC035
            PsyStateLookup[ModEntities[index]] = new HouseholdPsyState
            {
                HouseholdIndex = Households[index].Index,
                HouseholdVersion = Households[index].Version,
                DistrictLink_Index = -1,
                // 0f sentinel: ElapsedTime ≈ 0 on load, so (0 - 0 < 5) = no spike.
                // District link refreshes naturally within 5s. float.MinValue caused
                // massive immediate re-link for all entities on first tick after load.
                DistrictLink_LastUpdateTime = 0f,
                Resistance_LastUpdateTime = float.MinValue
            };
#pragma warning restore CIVIC035
        }
    }

    /// <summary>
    /// Manages HouseholdPsyState mod entity lifecycle — Phase 1 only (one-time bulk init on load).
    /// HouseholdPsyState lives on SEPARATE mod entities (not on vanilla Household entities).
    ///
    /// Duplicate-prevention via HasPsyState tag on vanilla Household entities:
    /// Query uses Exclude&lt;HasPsyState&gt; — ECS itself filters already-processed households.
    /// HasPsyState is NOT serialized (same pattern as PowerCapacityModifiers) —
    /// Phase 1 re-tags all households on each load.
    ///
    /// Phase 1 (one-time per load): Bulk create mod entities if modCount == 0,
    ///   ALWAYS tag untagged households (handles tag loss on save/load).
    ///
    /// Incremental creation (new households getting PropertyRenter) moved to
    /// MentalHealthResolverSystem as a lazy init pre-pass — zero ECB, EntityManager bulk,
    /// sync point is "free" (MHR already syncs for its own lookups).
    ///
    /// Bulk creation strategy (300k entities):
    /// - EntityManager.CreateEntity(archetype, count) = ~100ms sync point
    /// - Burst IJobParallelFor fills HouseholdIndex/Version = &lt;1ms parallel
    /// </summary>
    [ActIndependent]
    public partial class PsyImpactLifecycleSystem : CivicSystemBase
    {
        // Diagnostic counters (encapsulated to avoid CA2211)
        private static int s_BulkInitCount;   // EntityManager bulk create (not ECB)
        public static int BulkInitCount => s_BulkInitCount;
        public static void ResetCounters() { Interlocked.Exchange(ref s_BulkInitCount, 0); }
        private static void AddToBulkInitCount(int count) => Interlocked.Add(ref s_BulkInitCount, count);

        private static readonly LogContext Log = new("PsyImpactLifecycleSystem");

        // Housed households WITHOUT HasPsyState tag: creation policy remains renter-owned.
        private EntityQuery m_UntaggedHouseholdQuery;
        private EntityQuery m_UntaggedLiveHouseholdQuery;

        // Existing mod entities (for bulk init detection)
        private EntityQuery m_ModEntityQuery;

        // Not serialized: reset to false in Deserialize/ResetState so Phase 1 re-tags households after load
        [System.NonSerialized] private bool m_BulkInitDone;
        [System.NonSerialized] private int m_StableEmptyFrames;
        [System.NonSerialized] private int m_SlotAssignCounter;
        private EntityArchetype m_ModArchetype;
#pragma warning disable CIVIC269 // Write via IJobParallelFor, not direct indexer
        private ComponentLookup<HouseholdPsyState> m_PsyStateLookup;
#pragma warning restore CIVIC269

        private const int LOAD_STABLE_EMPTY_FRAMES = 3;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Archetype for mod entities: HouseholdPsyState + PsySlot
            // PsySlot in archetype = entities created with default slot 0, SetSharedComponent moves non-zero slots
            m_ModArchetype = EntityManager.CreateArchetype(typeof(HouseholdPsyState), typeof(PsySlot));

            // Housed households without HasPsyState tag — Phase 1 bulk init.
            m_UntaggedHouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.Exclude<HasPsyState>(),
                ComponentType.Exclude<Deleted>()
            );
            m_UntaggedLiveHouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.Exclude<HasPsyState>(),
                ComponentType.Exclude<Deleted>()
            );

            // Existing mod entities (for bulk init detection)
            m_ModEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<HouseholdPsyState>(),
                ComponentType.Exclude<Deleted>()
            );

            // Cached lookup for Burst fill job
            m_PsyStateLookup = GetComponentLookup<HouseholdPsyState>(false);

            Log.Info("Created (HasPsyState tagging)");
        }

        protected override void OnUpdateImpl()
        {
            // ════════════════════════════════════════════════════════════════
            // PHASE 1: One-time per load — bulk create + tag untagged households.
            // HasPsyState is NOT serialized, so after save/load ALL households
            // are untagged. Phase 1 re-tags them (and creates mod entities if needed).
            // ════════════════════════════════════════════════════════════════
            if (!m_BulkInitDone)
            {
                using (PerformanceProfiler.Measure("PILS.Phase1"))
                {
                    if (m_UntaggedHouseholdQuery.IsEmpty)
                    {
                        m_StableEmptyFrames++;
                        if (m_StableEmptyFrames >= LOAD_STABLE_EMPTY_FRAMES)
                        {
                            m_BulkInitDone = true;
                            Enabled = false; // Incremental creation after load stabilization is handled by MHR.
                            Log.Info("Phase 1 complete — self-disabled");
                        }
                        return;
                    }

                    m_StableEmptyFrames = 0;
                    if (!m_UntaggedHouseholdQuery.IsEmpty)
                    {
                        if (m_ModEntityQuery.IsEmpty)
                        {
                            BulkCreateModEntities(m_UntaggedHouseholdQuery);
                        }
                        else
                        {
                            // Entities loaded from save — ensure PsySlot assigned (not serialized)
                            EnsurePsySlots();
                            ReconcileLoadedHouseholds(m_UntaggedLiveHouseholdQuery, m_UntaggedHouseholdQuery);
                        }
                    }
                } // PILS.Phase1
                // NOTE M14: Orphaned mod entities (household count decreased between sessions) are NOT cleaned up here.
                // Runtime mitigated: ResolveHouseholdPsyJob and AggregateStatsJob skip orphans via PropertyRenter check.
                // Impact = wasted memory only. Orphans are cleaned on next save/load cycle (serialization skips them).
            }
        }

        /// <summary>
        /// Bulk create mod entities for all households in query.
        /// Uses EntityManager.CreateEntity (sync point) + Burst IJobParallelFor (parallel fill).
        /// </summary>
        [CompletesDependency("BulkCreateModEntities: Phase 1 bulk init materialises household entities into a NativeArray for Burst IJobParallelFor scheduling; runs once per world load until m_BulkInitDone, then self-disables")]
        private void BulkCreateModEntities(EntityQuery householdQuery)
        {
            var households = householdQuery.ToEntityArray(Allocator.Temp);
            try
            {
                BulkCreateAndTagHouseholds(households);
            }
            finally
            {
                if (households.IsCreated) households.Dispose();
            }
        }

        private void BulkCreateAndTagHouseholds(NativeArray<Entity> households)
        {
            int count = households.Length;
            if (count == 0)
                return;

            NativeArray<Entity> modEntities = default;
            NativeArray<Entity> householdsForJob = default;
            NativeArray<Entity> householdsToTag = default;
            bool disposalChainedToDependency = false;
            try
            {
                // Step 1: Bulk create mod entities (main thread sync point)
                modEntities = EntityManager.CreateEntity(m_ModArchetype, count, Allocator.TempJob);
                householdsForJob = new NativeArray<Entity>(count, Allocator.TempJob);
                householdsToTag = new NativeArray<Entity>(count, Allocator.Temp);
                NativeArray<Entity>.Copy(households, householdsForJob, count);
                NativeArray<Entity>.Copy(households, householdsToTag, count);

                // Step 2: Assign PsySlot round-robin (structural — moves between chunks, BEFORE fill job)
                // Skip slot 0 — default SharedComponent value, SetSharedComponent asserts on same-chunk move
                for (int i = 0; i < count; i++)
                {
                    int slot = (m_SlotAssignCounter + i) & 0x3; // 4 slots (0-3)
                    if (slot != 0)
                        EntityManager.SetSharedComponent(modEntities[i], new PsySlot { SlotIndex = slot });
                }
                m_SlotAssignCounter = (m_SlotAssignCounter + count) & 0x3;

                // Step 3: Fill HouseholdIndex/Version via Burst job (parallel, main thread free)
                m_PsyStateLookup.Update(this);
                var fillJob = new FillPsyModEntitiesJob
                {
                    ModEntities = modEntities,
                    Households = householdsForJob,
                    PsyStateLookup = m_PsyStateLookup
                };
                if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre FillPsyModEntitiesJob.Schedule count={count} modEntities={modEntities.IsCreated}/{modEntities.Length} householdsForJob={householdsForJob.IsCreated}/{householdsForJob.Length} householdsToTag={householdsToTag.IsCreated}/{householdsToTag.Length}");
                Dependency = fillJob.Schedule(count, 64, Dependency);
                if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post FillPsyModEntitiesJob.Schedule count={count} modEntities={modEntities.IsCreated}/{modEntities.Length} householdsForJob={householdsForJob.IsCreated}/{householdsForJob.Length} householdsToTag={householdsToTag.IsCreated}/{householdsToTag.Length}");

                // Dispose arrays after job completes
                if (householdsForJob.IsCreated) Dependency = householdsForJob.Dispose(Dependency);
                if (modEntities.IsCreated) Dependency = modEntities.Dispose(Dependency);
                disposalChainedToDependency = true;

                // Tag only households that now have a corresponding mod entity.
#pragma warning disable CIVIC006 // Bulk tag on vanilla entities — same pattern as MHR lazy init
                EntityManager.AddComponent<HasPsyState>(householdsToTag);
#pragma warning restore CIVIC006

                AddToBulkInitCount(count);
                Log.Info($"[BULK] Created {count} PsyState mod entities (total init: {s_BulkInitCount})");
            }
            finally
            {
                if (!disposalChainedToDependency)
                {
                    if (householdsForJob.IsCreated) householdsForJob.Dispose();
                    if (modEntities.IsCreated) modEntities.Dispose();
                }

                if (householdsToTag.IsCreated) householdsToTag.Dispose();
            }
        }

        [CompletesDependency("ReconcileLoadedHouseholds: Phase 1 post-load reconciliation materialises untagged households and existing mod-entity states into NativeArrays for hash-set diff; runs once per world load until m_BulkInitDone, then self-disables")]
        private void ReconcileLoadedHouseholds(EntityQuery untaggedLiveHouseholdQuery, EntityQuery untaggedRenterHouseholdQuery)
        {
            var households = untaggedLiveHouseholdQuery.ToEntityArray(Allocator.Temp);
            var renterHouseholds = untaggedRenterHouseholdQuery.ToEntityArray(Allocator.Temp);
            var states = m_ModEntityQuery.ToComponentDataArray<HouseholdPsyState>(Allocator.Temp);
            var existingKeys = new NativeParallelHashSet<long>(states.Length, Allocator.Temp);
            var existingToTag = new NativeList<Entity>(households.Length, Allocator.Temp);
            var missingToCreate = new NativeList<Entity>(renterHouseholds.Length, Allocator.Temp);

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

                for (int i = 0; i < renterHouseholds.Length; i++)
                {
                    var household = renterHouseholds[i];
                    if (!HouseholdPsyIdentity.IsValidHouseholdKey(household.Index, household.Version)
                        || !existingKeys.Contains(HouseholdPsyIdentity.MakeHouseholdKey(household.Index, household.Version)))
                        missingToCreate.Add(household);
                }

                if (existingToTag.Length > 0)
                {
#pragma warning disable CIVIC006 // Load-time bulk re-tag, no per-entity loop
                    EntityManager.AddComponent<HasPsyState>(existingToTag.AsArray());
#pragma warning restore CIVIC006
                }

                if (missingToCreate.Length > 0)
                {
                    BulkCreateAndTagHouseholds(missingToCreate.AsArray());
                    Log.Info($"[LOAD] Created {missingToCreate.Length} missing PsyState mod entities during reconciliation");
                }
            }
            finally
            {
                if (missingToCreate.IsCreated) missingToCreate.Dispose();
                if (existingToTag.IsCreated) existingToTag.Dispose();
                if (existingKeys.IsCreated) existingKeys.Dispose();
                if (states.IsCreated) states.Dispose();
                if (renterHouseholds.IsCreated) renterHouseholds.Dispose();
                if (households.IsCreated) households.Dispose();
            }
        }

        /// <summary>
        /// Ensure all existing mod entities have PsySlot assigned.
        /// Called on load — PsySlot (SharedComponent) is not serialized.
        /// </summary>
        [CompletesDependency("EnsurePsySlots: load-only structural repair; ToEntityArray materialises existing mod entities for round-robin PsySlot assignment after deserialize. Runs once per world load, not in hot path")]
        private void EnsurePsySlots()
        {
            if (m_ModEntityQuery.IsEmpty)
                return;

            // PsySlot requires per-entity SetSharedComponent (round-robin assignment)
            var existingMods = m_ModEntityQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < existingMods.Length; i++)
            {
                int slot = i & 0x3; // 4 slots (0-3)
                var psySlot = new PsySlot { SlotIndex = slot };
#pragma warning disable CIVIC051 // Load-only structural repair; shared component lookup API is unavailable here
                if (!EntityManager.HasComponent<PsySlot>(existingMods[i]))
#pragma warning restore CIVIC051
                    EntityManager.AddSharedComponent(existingMods[i], psySlot);
                else if (slot != 0)
                    EntityManager.SetSharedComponent(existingMods[i], new PsySlot { SlotIndex = slot });
            }

            Log.Info($"[LOAD] Ensured PsySlot on {existingMods.Length} existing mod entities");
            existingMods.Dispose();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
