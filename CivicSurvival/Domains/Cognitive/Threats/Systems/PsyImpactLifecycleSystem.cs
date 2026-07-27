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
    /// Burst job to fill PsyHouseholdLink + HouseholdPsyState defaults on freshly created
    /// mod entities. Uses IJobParallelFor with two NativeArrays (mod entities + households).
    /// [NativeDisableParallelForRestriction] safe: each ModEntities[i] is unique.
    /// Link and psy payload are written in the SAME Execute — no window where one is
    /// filled and the other still default (the orphan scan keys on the link).
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
        [NativeDisableParallelForRestriction]
        public ComponentLookup<PsyHouseholdLink> LinkLookup;

        public void Execute(int index)
        {
            // ModEntities are freshly created by us — guaranteed to have both components
#pragma warning disable CIVIC035
            LinkLookup[ModEntities[index]] = new PsyHouseholdLink
            {
                HouseholdIndex = Households[index].Index,
                HouseholdVersion = Households[index].Version
            };
            PsyStateLookup[ModEntities[index]] = new HouseholdPsyState
            {
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
    /// Manages HouseholdPsyState mod entity lifecycle — one-time bulk init on load.
    /// HouseholdPsyState lives on SEPARATE mod entities (not on vanilla Household entities).
    ///
    /// Duplicate-prevention via HasPsyState tag on vanilla Household entities:
    /// Query uses Exclude&lt;HasPsyState&gt; — ECS itself filters already-processed households.
    /// HasPsyState is NOT serialized (same pattern as PowerCapacityModifiers) —
    /// The bulk init re-tags all households on each load.
    ///
    /// One-time per load: bulk create mod entities if modCount == 0,
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

        // Not serialized: reset to false in Deserialize/ResetState so the bulk init re-tags households after load
        [System.NonSerialized] private bool m_BulkInitDone;
        /// <summary>
        /// True once Phase 1 (post-load bulk re-tag/reconcile) has completed for the current city.
        /// MentalHealthResolverSystem reads this to run its own restored-state reconcile only inside
        /// the post-load window — after Phase 1 no untagged household can match a restored PsyState key.
        /// </summary>
        public bool BulkInitDone => m_BulkInitDone;
        [System.NonSerialized] private int m_StableEmptyFrames;
        // Load-repair latch: PsySlot (SharedComponent) is not serialized, so restored mod
        // entities need it re-assigned exactly once per load — independent of whether any
        // household is still untagged. Reset in ResetState (load-boundary).
        [System.NonSerialized] private bool m_SlotsEnsured;
        private EntityArchetype m_ModArchetype;
#pragma warning disable CIVIC269 // Write via IJobParallelFor, not direct indexer
        private ComponentLookup<HouseholdPsyState> m_PsyStateLookup;
        private ComponentLookup<PsyHouseholdLink> m_LinkLookup;
#pragma warning restore CIVIC269

        private const int LOAD_STABLE_EMPTY_FRAMES = 3;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Archetype for mod entities: HouseholdPsyState + PsyHouseholdLink + PsySlot
            // PsySlot in archetype = entities created with default slot 0, SetSharedComponent moves non-zero slots
            m_ModArchetype = EntityManager.CreateArchetype(typeof(HouseholdPsyState), typeof(PsyHouseholdLink), typeof(PsySlot));

            // Housed households without HasPsyState tag — bulk init.
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

            // Existing mod entities (for bulk init detection). Keyed on the LINK: pre-split
            // sidecars restored without PsyHouseholdLink are invisible here by design — the
            // ValidateAfterLoad migration sweep in ModEntityCleanupSystem deletes them and
            // this system recreates fresh linked sidecars.
            m_ModEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<PsyHouseholdLink>(),
                ComponentType.Exclude<Deleted>()
            );

            // Cached lookups for Burst fill job
            m_PsyStateLookup = GetComponentLookup<HouseholdPsyState>(false);
            m_LinkLookup = GetComponentLookup<PsyHouseholdLink>(false);

            Log.Info("Created (HasPsyState tagging)");
        }

        protected override void OnUpdateImpl()
        {
            // ════════════════════════════════════════════════════════════════
            // PHASE 1: One-time per load — bulk create + tag untagged households.
            // HasPsyState is NOT serialized, so after save/load ALL households
            // are untagged. The bulk init re-tags them (and creates mod entities if needed).
            // ════════════════════════════════════════════════════════════════
            if (!m_BulkInitDone)
            {
                using (PerformanceProfiler.Measure("PILS.Phase1"))
                {
                    // Load repair first: restored mod entities need PsySlot (not serialized)
                    // regardless of whether any household is still untagged — the repair must
                    // not hide behind the renter query.
                    if (!m_SlotsEnsured)
                    {
                        EnsurePsySlots(); // no-op on a fresh city (mod query empty)
                        m_SlotsEnsured = true;
                    }

                    if (m_UntaggedHouseholdQuery.IsEmpty)
                    {
                        m_StableEmptyFrames++;
                        if (m_StableEmptyFrames >= LOAD_STABLE_EMPTY_FRAMES)
                        {
                            m_BulkInitDone = true;
                            Enabled = false; // Incremental creation after load stabilization is handled by PsySidecarCreateSystem.
                            Log.Info("Phase 1 complete — self-disabled");
                        }
                        // SAFETY-LOCK: no-op exit MUST leave Dependency = default. BeforeOnUpdate
                        // completes the previous final handle but does not zero it; without this
                        // reset AfterOnUpdate would stamp that stale COMPLETED handle as the
                        // write-fence of the registered RW types (PsyHouseholdLink +
                        // HouseholdPsyState via the fill lookups), replacing MHR's LIVE resolve
                        // fence with a done handle (readers stop waiting → data race) or
                        // re-poisoning the link fence with the input chain. Nothing was scheduled
                        // on this path, so skipping the stamp is legal.
                        // CIVIC184 false positive: no handle is lost — nothing was scheduled on
                        // this path and the previous tick's handle was completed by BeforeOnUpdate.
#pragma warning disable CIVIC184
                        Dependency = default;
#pragma warning restore CIVIC184
                        return;
                    }

                    m_StableEmptyFrames = 0;
                    if (m_ModEntityQuery.IsEmpty)
                    {
                        BulkCreateModEntities(m_UntaggedHouseholdQuery);
                    }
                    else
                    {
                        ReconcileLoadedHouseholds(m_UntaggedLiveHouseholdQuery, m_UntaggedHouseholdQuery);
                    }
                } // PILS.Phase1
                // NOTE M14: Orphaned mod entities (household count decreased between sessions) are NOT cleaned up here.
                // Runtime mitigated: ResolveHouseholdPsyJob and AggregateStatsJob skip orphans via PropertyRenter check.
                // Impact = wasted memory only. Orphans are collected by the ModEntityCleanupSystem link scan
                // (dead-household + 0:0 link detection) — serialization does NOT skip them.
            }

            // SAFETY-LOCK: same invariant as the no-op exit above. Both work legs
            // (BulkCreateModEntities / ReconcileLoadedHouseholds) force-complete everything they
            // schedule via the structural AddComponent before returning, so skipping the
            // AfterOnUpdate stamp is safe — and stamping a completed handle would overwrite the
            // live fences other systems rely on. If an async job is ever left in flight past this
            // point, this line must be revisited.
            // CIVIC184 false positive: no handle is lost — both work legs force-complete their
            // scheduled jobs via the structural AddComponent before returning here.
#pragma warning disable CIVIC184
            Dependency = default;
#pragma warning restore CIVIC184
        }

        /// <summary>
        /// Bulk create mod entities for all households in query.
        /// Uses EntityManager.CreateEntity (sync point) + Burst IJobParallelFor (parallel fill).
        /// </summary>
        [CompletesDependency("BulkCreateModEntities: Phase 1 bulk init materialises household entities into a NativeArray for Burst IJobParallelFor scheduling; PsySlot lands as one batched SetSharedComponent per contiguous quarter. Runs once per world load until m_BulkInitDone, then self-disables")]
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

                // Step 2: Assign PsySlot in contiguous quarters (structural — moves between
                // chunks, BEFORE the fill job). Entities come from m_ModArchetype already at
                // default PsySlot 0, so start at slot 1 and use Set (same-chunk); slot 0's
                // quarter is a no-op. Shared quarter layout lives in AssignSlotQuarters.
                AssignSlotQuarters(modEntities, startSlot: 1, addOrSet: false);

                // Step 3: Fill PsyHouseholdLink + psy defaults via Burst job (parallel, main thread free)
                m_PsyStateLookup.Update(this);
                m_LinkLookup.Update(this);
                var fillJob = new FillPsyModEntitiesJob
                {
                    ModEntities = modEntities,
                    Households = householdsForJob,
                    PsyStateLookup = m_PsyStateLookup,
                    LinkLookup = m_LinkLookup
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
                // PERF-LOCK: this structural AddComponent forces FillPsyModEntitiesJob to complete
                // before OnUpdate exits — no sidecar ever leaves this frame with the default 0:0
                // PsyHouseholdLink, which the orphan scan reads as "dead" and deletes. Making the
                // fill async past this point would let the scan destroy every fresh sidecar.
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
            var links = m_ModEntityQuery.ToComponentDataArray<PsyHouseholdLink>(Allocator.Temp);
            var existingKeys = new NativeParallelHashSet<long>(links.Length, Allocator.Temp);
            var existingToTag = new NativeList<Entity>(households.Length, Allocator.Temp);
            var missingToCreate = new NativeList<Entity>(renterHouseholds.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < links.Length; i++)
                {
                    if (HouseholdPsyIdentity.IsValidHouseholdKey(links[i].HouseholdIndex, links[i].HouseholdVersion))
                        existingKeys.Add(HouseholdPsyIdentity.MakeHouseholdKey(links[i].HouseholdIndex, links[i].HouseholdVersion));
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
                if (links.IsCreated) links.Dispose();
                if (renterHouseholds.IsCreated) renterHouseholds.Dispose();
                if (households.IsCreated) households.Dispose();
            }
        }

        /// <summary>
        /// Ensure all existing mod entities have PsySlot assigned.
        /// Called on load — PsySlot (SharedComponent) is not serialized.
        /// </summary>
        [CompletesDependency("EnsurePsySlots: load-only structural repair; ToEntityArray materialises restored mod entities, then one batched AddSharedComponent per slot assigns contiguous quarters (add-or-set on the NativeArray path). Runs once per world load, not in hot path")]
        private void EnsurePsySlots()
        {
            if (m_ModEntityQuery.IsEmpty)
                return;

            var mods = m_ModEntityQuery.ToEntityArray(Allocator.Temp);
            // Restored entities carry no PsySlot at all (SharedComponent is not serialized), so
            // start at slot 0 and use add-or-set (Add with SlotIndex = 0 is valid, unlike the
            // same-chunk Set that would assert). Shared quarter layout lives in AssignSlotQuarters.
            AssignSlotQuarters(mods, startSlot: 0, addOrSet: true);

            Log.Info($"[LOAD] Ensured PsySlot on {mods.Length} existing mod entities");
            mods.Dispose();
        }

        /// <summary>
        /// Assign PsySlot to <paramref name="entities"/> in contiguous quarters: slot i owns the
        /// index range [len*i/4, len*(i+1)/4). The slot mapping only needs balance (MHR walks all
        /// 4 slots per fire cycle), so quarters replace an interleaved round-robin and one batched
        /// call per slot replaces per-entity structural changes.
        /// <para><paramref name="startSlot"/> is 1 on the create path (entities come from
        /// m_ModArchetype already at default PsySlot 0, so slot 0's quarter is a no-op) and 0 on
        /// the load path (restored entities carry no PsySlot, so slot 0 must be assigned too).</para>
        /// <para><paramref name="addOrSet"/> picks AddSharedComponent (load: entity has no PsySlot
        /// yet — add-or-set, no same-chunk assert) over SetSharedComponent (create: entity already
        /// lives in the archetype). Both callers share this one quarter formula so a fresh city and
        /// a loaded city lay slots out identically.</para>
        /// </summary>
        private void AssignSlotQuarters(NativeArray<Entity> entities, int startSlot, bool addOrSet)
        {
            int len = entities.Length;
            for (int slot = startSlot; slot < 4; slot++)
            {
                int start = len * slot / 4;
                int subLen = len * (slot + 1) / 4 - start;
                if (subLen <= 0)
                    continue;

                var sub = entities.GetSubArray(start, subLen);
                var psySlot = new PsySlot { SlotIndex = slot };
                if (addOrSet)
                    EntityManager.AddSharedComponent(sub, psySlot);
                else
                    EntityManager.SetSharedComponent(sub, psySlot);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
