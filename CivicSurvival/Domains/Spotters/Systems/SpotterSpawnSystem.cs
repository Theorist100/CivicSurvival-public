using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using System;
using System.Collections.Concurrent;
using System.Threading;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Spawns OSINT spotters (Valera) in the city.
    ///
    /// Spotters appear:
    /// - Passively over time (base rate 1/14 days, faster with low integrity)
    /// - Near missile impacts (feedback loop)
    /// - During VIP visibility or long blackouts
    ///
    /// IMPORTANT: Creates SEPARATE entities with SpotterData.
    /// NEVER AddComponent to vanilla buildings (causes homeless spike cascade).
    ///
    /// PERF: Throttled to 500ms - spotters spawn rarely.
    /// </summary>
    [ActIndependent]
#pragma warning disable CIVIC228 // Event handlers enqueue thread-safe intents and ForceNextUpdate; RNG/throttle state stays in OnThrottledUpdate
    public partial class SpotterSpawnSystem : ThrottledSystemBase, IResettable
#pragma warning restore CIVIC228
    {
        // ECB command counter (encapsulated to avoid CA2211)
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private static readonly LogContext Log = new("SpotterSpawnSystem");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        // Queries
        private EntityQuery m_SpotterQuery;
        private EntityQuery m_ResidentialQuery;
        private EntityQuery m_CountermeasuresStateQuery;

        // ECB for structural changes (creating mod entities)
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        // ComponentLookups
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private BufferLookup<CognitiveIntegrityBuffer> m_CogIntegrityBufferLookup;
        private BufferLookup<EvacuatedReturnBuffer> m_EvacReturnBufferLookup;
        private EntityStorageInfoLookup m_BuildingStorageLookup;

        // Time provider
        private GameTimeSystem? m_TimeProvider;

        // State (serialized)
        private float m_SpawnTimer;
        private SerializableRandom m_Random;
        [System.NonSerialized] private bool m_Initialized; // Not serialized: re-initialized in OnStartRunning

        // Cached spotter count (updated once per OnThrottledUpdate, avoids repeated sync points)
        private int m_CachedSpotterCount;
        private int m_PendingSpawns;

        // Packed BuildingIndex+BuildingVersion keys for all active spotters.
        // Rebuilt in OnThrottledUpdate (own context) so TrySpawnSpotter can be called safely
        // from SpotterAggregateSystem without cross-system SystemAPI.Query (CRIT-01).
        // Version IS checked at usage (TryGetValue + spotterVersion != candidate.Version).
        [NonEntityIndex] private NativeHashSet<long> m_CachedBuildingsWithSpotters;

        // Deferred event-triggered spawn counts — handlers only enqueue; this system owns RNG/throttle state.
        private int m_PendingImpactSpawnCount;
        [System.NonSerialized] private readonly ConcurrentQueue<int> m_PendingBlackoutDistricts = new();
        [System.NonSerialized] private readonly ConcurrentQueue<int> m_PendingVipDistricts = new();
        [System.NonSerialized] private int m_EventDrainCursor;

        private const int MAX_EVENT_SPAWNS_PER_TICK = 2;
        private const int MAX_TIMER_CATCHUP_TICKS = 7;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_TimeProvider = GameTimeSystem.Instance;

            // Mix TickCount so seed is unique per session even when TotalGameHours=0 (new game)
            double gameHours;
            if (m_TimeProvider != null)
                gameHours = m_TimeProvider.Current.TotalGameHours;
            else
            {
                Mod.Log.Warn("SpotterSpawnSystem.OnCreate: GameTimeSystem unavailable — using TickCount-only seed");
                gameHours = 0f;
            }
            int seed = unchecked((int)(gameHours * GameRate.SECONDS_PER_HOUR) ^ Environment.TickCount);
            m_Random = new SerializableRandom(seed);

            // SpotterData is on SEPARATE entities (not on vanilla buildings)
            m_SpotterQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterData>(),
                ComponentType.Exclude<Deleted>()
            );

            // Residential buildings (no exclusion needed - SpotterData on separate entity)
            m_ResidentialQuery = GetEntityQuery(
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Abandoned>(),
                ComponentType.Exclude<Condemned>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<UnderConstruction>()
            );
            m_CountermeasuresStateQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterCountermeasuresState>()
            );

            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_CogIntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);
            m_EvacReturnBufferLookup = GetBufferLookup<EvacuatedReturnBuffer>(false);
            m_BuildingStorageLookup = GetEntityStorageInfoLookup();
            m_CachedBuildingsWithSpotters = new NativeHashSet<long>(16, Allocator.Persistent);

            // Subscribe to threat impacts - Valera posts about damage
            SubscribeRequired<ThreatImpactEvent>(OnThreatImpact);

            // FIX S7-1: Subscribe to blackout events for spotter spawning
            SubscribeRequired<LongBlackoutEvent>(OnLongBlackoutEvent);
            SubscribeRequired<VIPVisibleDuringBlackoutEvent>(OnVIPVisibleEvent);

            Log.Info($"Created with seed {seed}");
        }

        [CompletesDependency("OnThrottledUpdate spotter cache: m_CachedSpotterCount feeds spawn rate-limiting; CalculateEntityCount runs once per throttle tick, sync amortised over throttle interval")]
        protected override void OnThrottledUpdate()
        {
            m_CurrentDistrictLookup.Update(this);
            m_CogIntegrityBufferLookup.Update(this);
            m_EvacReturnBufferLookup.Update(this);
            m_BuildingStorageLookup.Update(this);
            m_TimeProvider ??= GameTimeSystem.Instance;

            m_CachedSpotterCount = m_SpotterQuery.CalculateEntityCount();
            m_PendingSpawns = 0;

            // Rebuild buildings-with-spotters map in own context (safe SystemAPI.Query).
            // TrySpawnSpotter uses this cache — no cross-system SystemAPI.Query needed (CRIT-01).
            m_CachedBuildingsWithSpotters.Clear();
            foreach (var spotter in SystemAPI.Query<RefRO<SpotterData>>().WithNone<Deleted>())
            {
                if (spotter.ValueRO.IsEvacuating)
                    continue;
                m_CachedBuildingsWithSpotters.Add(PackBuildingId(spotter.ValueRO.Building.Index, spotter.ValueRO.Building.Version));
            }

            if (!m_Initialized)
            {
                m_SpawnTimer = 0f;
                m_Initialized = true;
            }

            var spotterCfg = BalanceConfig.Current.Spotter;
            int spawnBudget = MAX_EVENT_SPAWNS_PER_TICK;
            var residentialSnapshot = default(NativeList<Entity>);
            try
            {
                if (m_TimeProvider != null)
                    ProcessEvacuatedReturns(spotterCfg, m_TimeProvider.Current.TotalGameHours, ref residentialSnapshot);

                DrainEventSpawnSources(spotterCfg, ref spawnBudget, ref residentialSnapshot);

                using (PerformanceProfiler.Measure("SpotterSpawnSystem.OnUpdate"))
                {
                    // Accumulate deltaTime based on throttle interval
                    float deltaTime = ThrottledDeltaSeconds;
                    m_SpawnTimer += deltaTime;

                    // Base spawn check (interval scaled by city integrity)
                    // Lower integrity = faster spotter spawns
                    // NEG-FIX: Guard against negative/zero spawn interval
                    float spawnDays = math.max(spotterCfg.BaseSpawnIntervalDays, 1f);
                    float baseIntervalSeconds = spawnDays * GameRate.SECONDS_PER_DAY;
                    float integrityFactor = GetCityIntegrityFactor();
                    float effectiveInterval = baseIntervalSeconds * integrityFactor;

#pragma warning disable CIVIC246 // effectiveInterval has minimum from config (never < 30s)
                    int catchup = 0;
                    while (m_SpawnTimer >= effectiveInterval && catchup < MAX_TIMER_CATCHUP_TICKS)
#pragma warning restore CIVIC246
                    {
                        m_SpawnTimer -= effectiveInterval;
                        _ = TrySpawnSpotter(integrityFactor < 0.5f ? "low_integrity" : "background", -1, ref residentialSnapshot);
                        catchup++;
                    }
                    if (catchup == MAX_TIMER_CATCHUP_TICKS && m_SpawnTimer >= effectiveInterval)
                    {
                        Log.Warn($"Spawn timer catchup capped at {MAX_TIMER_CATCHUP_TICKS} ticks");
                        m_SpawnTimer = effectiveInterval;
                    }
                }
            }
            finally
            {
                if (residentialSnapshot.IsCreated)
                    residentialSnapshot.Dispose();
            }

            // Register this system's Dependency with the barrier once per throttled update.
            // Done here (not in SpawnSpotterAt) so Dependency always belongs to this system's context.
            if (m_PendingSpawns > 0)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        // ============================================================================
        // PUBLIC API (called by other systems/domains)
        // ============================================================================

        /// <summary>
        /// Called when VIP district is visible during blackout.
        /// </summary>
        public void OnVIPVisible(int districtIndex)
        {
            m_PendingVipDistricts.Enqueue(districtIndex);
        }

        /// <summary>
        /// Called when blackout lasts more than 4 hours.
        /// </summary>
        public void OnLongBlackout(int districtIndex)
        {
            m_PendingBlackoutDistricts.Enqueue(districtIndex);
        }

        // ============================================================================
        // SPAWN LOGIC
        // ============================================================================

        private void ProcessEvacuatedReturns(SpotterConfig spotterCfg, double currentTime, ref NativeList<Entity> residentialSnapshot)
        {
            if (!m_CountermeasuresStateQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var singletonEntity))
                return;
            if (!m_EvacReturnBufferLookup.TryGetBuffer(singletonEntity, out var buffer))
                return;

            double maxReturnWaitHours = spotterCfg.EvacReturnStaleDays * GameRate.HOURS_PER_DAY;

            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                if (currentTime < buffer[i].ReturnTime)
                    continue;

                bool spawned = TrySpawnSpotter("evacuation_return", -1, ref residentialSnapshot);
                if (spawned)
                {
                    buffer.RemoveAt(i);

                    Log.Info("Evacuated spotter returned!");
                    EventBus?.SafePublish(new NarrativeTriggerEvent(
                        NarrativeTrigger.SpotterReturn.ToKey()), "SpotterSpawnSystem");
                    continue;
                }

                if (currentTime > buffer[i].ReturnTime + maxReturnWaitHours)
                {
                    Log.Warn("Evacuated spotter return expired — no owner-context spawn slot became available");
                    buffer.RemoveAt(i);
                    continue;
                }

                Log.Warn("Evacuated spotter return deferred — no owner-context spawn slot available");
            }
        }

        private bool TrySpawnSpotter(string reason, int preferredDistrictIndex, ref NativeList<Entity> residentialEntities)
        {
            // Update(this) was removed here — calling it from SpotterAggregateSystem context
            // causes ECS safety violation (this=SpotterSpawnSystem in wrong system's OnUpdate).
            // Lookup is refreshed at start of OnThrottledUpdate. Cross-system callers accept
            // up to 1 throttle-window of staleness (max 10 frames) as trade-off.
            // m_CachedSpotterCount is set in OnThrottledUpdate — no CalculateEntityCount() sync point here.
            int currentCount = m_CachedSpotterCount + m_PendingSpawns;
            if (currentCount >= BalanceConfig.Current.Spotter.MaxSpotters)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Max spotters reached ({currentCount})");
                return false;
            }

            if (!EnsureResidentialSnapshot(ref residentialEntities))
            {
                return false;
            }

            if (residentialEntities.Length == 0)
            {
                return false;
            }

            // Find a building without spotter (try up to 10 times).
            // Uses m_CachedBuildingsWithSpotters — rebuilt each OnThrottledUpdate in own context.
            Entity building = Entity.Null;
            if (preferredDistrictIndex >= 0)
            {
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    int randomIndex = m_Random.Next(0, residentialEntities.Length);
                    Entity candidate = residentialEntities[randomIndex];
                    if (GetDistrictIndex(candidate) != preferredDistrictIndex)
                        continue;

#pragma warning disable CIVIC097 // Packed Index+Version key; not a raw Entity.Index lookup.
                    if (!m_CachedBuildingsWithSpotters.Contains(PackBuildingId(candidate.Index, candidate.Version)))
#pragma warning restore CIVIC097
                    {
                        building = candidate;
                        break;
                    }
                }
            }

            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (building != Entity.Null)
                    break;

                int randomIndex = m_Random.Next(0, residentialEntities.Length);
                Entity candidate = residentialEntities[randomIndex];

#pragma warning disable CIVIC097 // Packed Index+Version key; not a raw Entity.Index lookup.
                if (!m_CachedBuildingsWithSpotters.Contains(PackBuildingId(candidate.Index, candidate.Version)))
#pragma warning restore CIVIC097
                {
                    building = candidate;
                    break;
                }
            }

            if (building == Entity.Null)
            {
                Log.Debug("Could not find building without spotter after 10 attempts");
                return false;
            }

            SpawnSpotterAt(building, reason);
            return true;
        }

        [CompletesDependency("Built at most once per throttled spawn pass (snapshot is cached via the IsCreated guard) and only when a spawn is actually needed; uses a cached query because the spawn path runs from a cross-system caller's context where SystemAPI.Query would bind to the wrong system, so residentials are materialised via ToEntityArray")]
        private bool EnsureResidentialSnapshot(ref NativeList<Entity> residentialEntities)
        {
            if (residentialEntities.IsCreated)
                return true;

            if (m_ResidentialQuery.IsEmpty)
            {
                Log.Debug("No residential buildings available");
                return false;
            }

            using var entities = m_ResidentialQuery.ToEntityArray(Allocator.Temp);
            residentialEntities = new NativeList<Entity>(entities.Length, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                residentialEntities.Add(entities[i]);
            }
            return true;
        }

        private void SpawnSpotterAt(Entity building, string reason)
        {
            if (!m_BuildingStorageLookup.Exists(building))
            {
                Log.Debug("Building entity destroyed before spotter spawn");
                return;
            }

            int districtIndex = GetDistrictIndex(building);

            // Create SEPARATE entity with SpotterData (NEVER AddComponent on vanilla!)
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            var spotterEntity = ecb.CreateEntity();
            ecb.AddComponent(spotterEntity, new SpotterData
            {
                Building = BuildingRef.FromEntity(building),
                PenaltyContribution = BalanceConfig.Current.Spotter.PenaltyPerSpotter,
                IsActive = true,
                ReactivateTime = 0,
                DistrictIndex = districtIndex
            });
            IncrementEcbCount();
            // NOTE: AddJobHandleForProducer is NOT called here — moved to OnThrottledUpdate so
            // that this.Dependency is always from the correct system context (M16 fix).
            // Cross-system callers (SpotterAggregateSystem) are managed systems with no Burst jobs,
            // so Dependency=default is always complete; ECB playback is unaffected.

            if (districtIndex == -1)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Spawned GLOBAL spotter (no district) - reason: {reason}");
            }

            // R9-H4: Update cache so same building is not picked again in same throttled update
            // CIVIC097: NativeHashMap<int,int> stores Index→Version pair; Version validated at lookup (line 277-278)
#pragma warning disable CIVIC097
            m_CachedBuildingsWithSpotters.Add(PackBuildingId(building.Index, building.Version));
#pragma warning restore CIVIC097
            m_PendingSpawns++;
            int totalSpotters = m_CachedSpotterCount + m_PendingSpawns;
            Log.Info($"New spotter spawned! Reason: {reason}, Total: {totalSpotters}");

            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.SpotterSpawn.ToKey()), "SpotterSpawnSystem");
        }

        // ============================================================================
        // EVENT HANDLERS
        // ============================================================================

        /// <summary>
        /// Handle ThreatImpactEvent - Valera posts about the impact.
        /// PERF: Use random spawn instead of spatial search (was 10ms per call!)
        /// </summary>
        private void OnThreatImpact(ThreatImpactEvent evt)
        {
            Interlocked.Increment(ref m_PendingImpactSpawnCount);
        }

        /// <summary>
        /// FIX S7-1: Handle LongBlackoutEvent — blackout > threshold hours in a district.
        /// </summary>
        private void OnLongBlackoutEvent(LongBlackoutEvent evt)
        {
            OnLongBlackout(evt.DistrictIndex);
        }

        /// <summary>
        /// FIX S7-1: Handle VIPVisibleDuringBlackoutEvent — VIP district has power while others are dark.
        /// </summary>
        private void OnVIPVisibleEvent(VIPVisibleDuringBlackoutEvent evt)
        {
            OnVIPVisible(evt.DistrictIndex);
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        private int GetDistrictIndex(Entity building)
        {
            if (building == Entity.Null)
                return -1;

            if (m_CurrentDistrictLookup.TryGetComponent(building, out var district))
            {
                return district.m_District.Index;
            }

            return -1;
        }

        /// <summary>
        /// Get city-wide integrity factor for spawn interval scaling.
        /// Returns 0.1-1.0 where lower = faster spawns.
        /// </summary>
        private float GetCityIntegrityFactor()
        {
            if (!SystemAPI.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return 1f;

            if (!m_CogIntegrityBufferLookup.TryGetBuffer(stateEntity, out var buffer))
                return 1f;
            if (buffer.Length == 0)
                return 1f;

            float totalIntegrity = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                totalIntegrity += buffer[i].Integrity;
            }
            float avgIntegrity = totalIntegrity / buffer.Length;

            return math.clamp(avgIntegrity, 0.1f, 1f);
        }

        private void DrainEventSpawns(ref int counter, float chance, string reason, int preferredDistrictIndex, ref int spawnBudget, ref NativeList<Entity> residentialSnapshot)
        {
            if (spawnBudget <= 0)
                return;

            int count = Interlocked.Exchange(ref counter, 0);
            while (count > 0 && spawnBudget > 0)
            {
                count--;
                if (m_Random.NextFloat() < chance)
                {
                    _ = TrySpawnSpotter(reason, preferredDistrictIndex, ref residentialSnapshot);
                    spawnBudget--;
                }
            }

            if (count > 0)
                Interlocked.Add(ref counter, count);
        }

        private void DrainEventSpawnSources(
            SpotterConfig spotterCfg,
            ref int spawnBudget,
            ref NativeList<Entity> residentialSnapshot)
        {
            int start = m_EventDrainCursor;
            for (int offset = 0; offset < 3 && spawnBudget > 0; offset++)
            {
                switch ((start + offset) % 3)
                {
                    case 0:
                        DrainEventSpawns(ref m_PendingImpactSpawnCount, spotterCfg.SpawnOnImpactChance, "impact", -1, ref spawnBudget, ref residentialSnapshot);
                        break;
                    case 1:
                        DrainDistrictEventSpawns(m_PendingBlackoutDistricts, spotterCfg.SpawnOnBlackoutChance, "blackout", ref spawnBudget, ref residentialSnapshot);
                        break;
                    case 2:
                        DrainDistrictEventSpawns(m_PendingVipDistricts, spotterCfg.SpawnOnVipChance, "vip", ref spawnBudget, ref residentialSnapshot);
                        break;
                }
            }

            m_EventDrainCursor = (start + 1) % 3;
        }

        private void DrainDistrictEventSpawns(ConcurrentQueue<int> queue, float chance, string reason, ref int spawnBudget, ref NativeList<Entity> residentialSnapshot)
        {
            while (spawnBudget > 0 && queue.TryDequeue(out int districtIndex))
            {
                if (m_Random.NextFloat() < chance)
                {
                    _ = TrySpawnSpotter(reason, districtIndex, ref residentialSnapshot);
                    spawnBudget--;
                }
            }
        }

        private static long PackBuildingId(int index, int version)
        {
            return ((long)index << 32) | (uint)version;
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        /// <summary>
        /// Reset spawn state for new game.
        /// </summary>
        public void ResetState()
        {
            m_SpawnTimer = 0f;
            m_Random = new SerializableRandom(DESERIALIZE_RANDOM_FALLBACK_SEED);
            m_Initialized = false;
            Interlocked.Exchange(ref m_PendingImpactSpawnCount, 0);
            DrainPendingDistrictQueue(m_PendingBlackoutDistricts);
            DrainPendingDistrictQueue(m_PendingVipDistricts);
            m_EventDrainCursor = 0;
            if (m_CachedBuildingsWithSpotters.IsCreated) m_CachedBuildingsWithSpotters.Clear();
            m_CachedSpotterCount = 0;
            m_PendingSpawns = 0;
            Log.Info("State reset");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ThreatImpactEvent>(OnThreatImpact);
            // FIX S7-1: Unsubscribe from blackout events
            UnsubscribeSafe<LongBlackoutEvent>(OnLongBlackoutEvent);
            UnsubscribeSafe<VIPVisibleDuringBlackoutEvent>(OnVIPVisibleEvent);
            if (m_CachedBuildingsWithSpotters.IsCreated) m_CachedBuildingsWithSpotters.Dispose();
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
