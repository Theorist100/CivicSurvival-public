using System.Collections.Generic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Data;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Migrates refugees from border (OutsideConnection) to park shelters.
    /// Runs periodically to check if new parks were built and move refugees.
    ///
    /// Flow:
    /// 1. Query HomelessHousehold entities where m_TempHome is OutsideConnection
    /// 2. If Park entities exist, update m_TempHome to first Park
    /// 3. Post positive Chirper message about migration
    /// </summary>
    [ActIndependent]
    public partial class RefugeeMigrationSystem : CivicSystemBase
    {
        private const int BATCH_INDEX_WRAP = 10000;

        private static readonly LogContext Log = new("RefugeeMigrationSystem");

        private EntityQuery m_ParkQuery;
        private EntityQuery m_OutsideConnectionQuery;
        private EntityQuery m_NeedsRelocationQuery;
        // Post-load re-derive: all refugees carrying both markers (enabled-bit agnostic).
        private EntityQuery m_RefugeeAllQuery;

        // S15-4 FIX: Lookup to distinguish our refugees from vanilla homeless
        private ComponentLookup<RefugeeHousehold> m_RefugeeHouseholdLookup;
        // m_HomelessLookup (post-load re-derive) is declared in the Serialization partial,
        // co-located with its only user (ValidateAfterLoad). It is initialized below.

        // ECB barrier for the structural RemoveComponent<NeedsRefugeeRelocation> that
        // closes the presence gate once a refugee lands in a live park.
        private GameSimulationEndBarrier m_ECBSystem = null!;

        private double m_LastCheckGameHours;
        private int m_MigrationBatchIndex;
        // Persisted random state — ensures park selection varies across sessions and save/loads
        private int m_RandomState;
        private Unity.Mathematics.Random m_Random;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ParkQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Park>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>()
            );

            m_OutsideConnectionQuery = GetEntityQuery(
                ComponentType.ReadOnly<OutsideConnection>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>()
            );

            // Gate: tick only while at least one refugee CARRIES the relocation marker
            // (border-waiting or orphaned). Presence-based — ShouldRunSystem uses
            // IsEmptyIgnoreFilter (matching-chunk count, ignores enableable bits), so the
            // marker must be a plain tag that is Added/Removed, not enabled/disabled. Once
            // every refugee sits in a live park the marker is gone everywhere, the matching
            // chunk drains and the system stops being scheduled.
            m_NeedsRelocationQuery = GetEntityQuery(
                ComponentType.ReadOnly<NeedsRefugeeRelocation>(),
                ComponentType.ReadOnly<HomelessHousehold>(),
                ComponentType.Exclude<Deleted>()
            );
            RequireForUpdate(m_NeedsRelocationQuery);

            // Post-load reconcile: every refugee, matched by the two durable markers only
            // (RefugeeHousehold + HomelessHousehold, both always present). NeedsRefugeeRelocation
            // must NOT be in this match — the reconcile decides whether to Add or Remove it, so
            // it has to see refugees both with and without the marker. RequireForUpdate is NOT
            // placed on this — it is read only by ValidateAfterLoad.
            m_RefugeeAllQuery = GetEntityQuery(
                ComponentType.ReadOnly<RefugeeHousehold>(),
                ComponentType.ReadOnly<HomelessHousehold>(),
                ComponentType.Exclude<Deleted>()
            );

            m_RefugeeHouseholdLookup = GetComponentLookup<RefugeeHousehold>(true);
            m_HomelessLookup = GetComponentLookup<HomelessHousehold>(true);

            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // Initialize random with mixed session inputs; persisted state takes over after load.
            // OnCreate fires before GameTimeSystem activation — use TryGet, 0 fallback is fine
            // (other entropy inputs keep the seed session-unique).
            uint gameSeconds = GameTimeSystem.TryGetTotalGameSeconds(out var seconds)
                ? (uint)seconds
                : 0u;
            uint seed = Unity.Mathematics.math.hash(new Unity.Mathematics.uint4(
                gameSeconds,
                (uint)System.Environment.TickCount,
                (uint)World.GetHashCode(),
                0xC15C0DEu));
            m_Random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);

            Log.Info("Created");
        }

        [CompletesDependency("OnUpdateImpl: throttled (m_LastCheckGameHours interval) migration pass; ToEntityArray on Park query for random-index spawn pick. Off the hot path — RefugeeMigrationSystem is not a [HotPathSystem]")]
        protected override void OnUpdateImpl()
        {
            // LOAD-INVARIANT: OnUpdate can run before GameTime activation on the first loaded frame.
            if (!GameTimeSystem.TryGetGameHours(out var currentGameHours))
                return;
            // BUG-R-016 FIX: Use Balance constant instead of magic number
            var scenarioCfg = BalanceConfig.Current.Scenario;
            if (currentGameHours - m_LastCheckGameHours < scenarioCfg.MigrationCheckIntervalHours)
                return;

            m_LastCheckGameHours = currentGameHours;

            // Skip if no parks
            if (m_ParkQuery.IsEmptyIgnoreFilter)
                return;

            // RequireForUpdate(m_NeedsRelocationQuery) already guarantees ≥1 enabled
            // marker, so a separate "no homeless" guard is redundant here.

            // Get random park for migration target
            var parks = m_ParkQuery.ToEntityArray(Allocator.Temp);
            if (parks.Length == 0)
            {
                if (parks.IsCreated) parks.Dispose();
                return;
            }

            // S15-4 FIX: Build park set for orphan detection (destroyed park → reassign)
            var parkSet = new NativeHashSet<Entity>(parks.Length, Allocator.Temp);
            for (int i = 0; i < parks.Length; i++)
                parkSet.Add(parks[i]);

            // Build outside connection set for border detection
            CountForCapacity(m_OutsideConnectionQuery, out int connCount);
            if (connCount == 0)
            {
                if (parks.IsCreated) parks.Dispose();
                if (parkSet.IsCreated) parkSet.Dispose();
                return;
            }

            var connectionSet = new NativeHashSet<Entity>(connCount, Allocator.Temp);
            foreach (var (_, connEntity) in
                SystemAPI.Query<RefRO<OutsideConnection>>()
                .WithAll<Transform>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                connectionSet.Add(connEntity);
            }

            m_RefugeeHouseholdLookup.Update(this);

            // Single pass over ONLY marked households (WithAll on the presence marker =
            // those flagged for relocation). Border and orphan keep separate budgets so
            // neither starves the other (identical semantics to the former two-pass split),
            // but the scan no longer walks every HomelessHousehold in the city. The marker
            // is REMOVED (structural, via ECB) the moment m_TempHome lands in a live park,
            // which (once all refugees are housed) drains the gate query's matching chunk
            // and stops the system from being scheduled at all.
            var ecb = m_ECBSystem.CreateCommandBuffer();
            int migratedCount = 0;
            int orphanCount = 0;
            int maxPerUpdate = scenarioCfg.MigrationMaxPerUpdate;

            foreach (var (homelessRef, entity) in
                SystemAPI.Query<RefRW<HomelessHousehold>>()
                .WithAll<NeedsRefugeeRelocation>()   // presence: gate set
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                // Ownership boundary: the marker is ours alone, but keep the refugee check
                // as a defensive guard so a vanilla homeless can never be relocated.
                if (!m_RefugeeHouseholdLookup.HasComponent(entity))
                    continue;

                Entity tempHome = homelessRef.ValueRO.m_TempHome;
                bool atBorder = connectionSet.Contains(tempHome);
                bool atLivePark = parkSet.Contains(tempHome);

                if (atLivePark)
                {
                    // Already in a live park — remove the stale marker (desync guard) and skip.
                    ecb.RemoveComponent<NeedsRefugeeRelocation>(entity);
                    continue;
                }

                // border OR orphan (neither in a connection nor a live park) → relocate,
                // each from its own per-update budget so border keeps priority over orphan.
                if (atBorder && migratedCount >= maxPerUpdate)
                    continue;
                if (!atBorder && orphanCount >= maxPerUpdate)
                    continue;

                Entity park = PickPark(parks);
                homelessRef.ValueRW.m_TempHome = park;
                AddToRenterBuffer(ecb, park, entity);
                ecb.RemoveComponent<NeedsRefugeeRelocation>(entity); // gate closed for this refugee
                if (atBorder) migratedCount++; else orphanCount++;
            }

            if (parks.IsCreated) parks.Dispose();
            if (connectionSet.IsCreated) connectionSet.Dispose();
            if (parkSet.IsCreated) parkSet.Dispose();

            if (migratedCount > 0)
            {
                // BUG-R-017 FIX: Reset batch index on overflow to prevent int32 wraparound
                m_MigrationBatchIndex = (m_MigrationBatchIndex + 1) % BATCH_INDEX_WRAP;

                // Post positive Chirper message
                // BUG-R-014 FIX: Reduce allocations with helper method for single-arg events
                var eventArgs = NarrativeArgs.OneArg(migratedCount);
                EventBus?.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.RefugeeBus.ToKey(),
                    eventArgs
                ), "RefugeeMigrationSystem");

                // BUG-R-015 FIX: Use Debug for frequent internal tracking logs
                if (Log.IsDebugEnabled) Log.Debug($"Migrated {migratedCount} households from border to park");
            }

            if (orphanCount > 0)
            {
                Log.Warn($"S15-4: Reassigned {orphanCount} orphaned refugee households to park (destroyed shelter)");
            }
        }

        private Entity PickPark(NativeArray<Entity> parks)
        {
            if (parks.Length == 0)
                return Entity.Null;
            int parkIndex = m_Random.NextInt(0, parks.Length);
            return parks[parkIndex];
        }

        /// <summary>
        /// Register the household in the park's Renter buffer — vanilla counts shelter
        /// residents from it (ResidentsSection), and Game.Serialization.RenterSystem
        /// rebuilds it from m_TempHome on load, so this keeps live state canonical.
        /// Duplicate-safe, mirroring vanilla RenterSystem.AddRenter. The append is
        /// deferred through the ECB (symmetric with the spawn path) so the vanilla
        /// buffer is never mutated directly on the main thread; the dedup read stays
        /// as a defensive desync guard.
        /// </summary>
        private void AddToRenterBuffer(EntityCommandBuffer ecb, Entity park, Entity household)
        {
            if (park == Entity.Null || !SystemAPI.HasBuffer<Renter>(park))
                return;

            var renters = SystemAPI.GetBuffer<Renter>(park);
            for (int i = 0; i < renters.Length; i++)
            {
                if (renters[i].m_Renter == household)
                    return;
            }
            ecb.AppendToBuffer(park, new Renter { m_Renter = household });
        }
    }
}
