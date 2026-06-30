using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Services;
using CivicSurvival.Core.Systems.Base;
using B = CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;
using ArchetypeData = Game.Prefabs.ArchetypeData;
using DynamicHousehold = Game.Prefabs.DynamicHousehold;
using HouseholdData = Game.Prefabs.HouseholdData;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Refugee Spawn System for Village scenario.
    ///
    /// PreWar refugee arc (war reaches the village ~20 days after scenario start):
    /// - Phase A (no park yet): slow border trickle (population-tier rate × act
    ///   multiplier), nag messages push the player to build a park.
    /// - Phase B (first park built): the target is anchored — +RefugeeTargetPercent
    ///   of the population at that moment, delivered over RefugeeInfluxDurationHours.
    ///   Pre-park arrivals count toward the target.
    /// - End: target reached, or the war starts (RefugeeInfluxCoordinator calls
    ///   EndInflux when the PreWar act ends — the road to the village is cut).
    /// Arrived refugees never leave: RefugeeRetentionSystem strips vanilla MovingAway.
    ///
    /// This is the INVERTED exodus - village receives people instead of losing them.
    ///
    /// S16-02 ACCEPTED: Act multiplier polled on hourly tick — max 1h delay for rate change.
    /// Hourly granularity matches refugee spawn rate design. Sub-hour proration = overengineering.
    /// </summary>
#pragma warning disable CA1001 // ECS systems use OnDestroy for cleanup, not IDisposable
#pragma warning disable CIVIC317 // Modal reset is centralized in PostLoadValidationSystem.OnGameLoaded
    [ActIndependent]
    public partial class RefugeeSpawnSystem : CivicUISystemBase
#pragma warning restore CIVIC317
#pragma warning restore CA1001
    {
        private static readonly LogContext Log = new("RefugeeSpawnSystem");

        // ===== State (field order must match binary serialization order) =====
        private bool m_Active;
        private int m_HoursElapsed;
        private int m_TotalRefugeesAdded;
        private int m_SpawnCounter;
        private int m_OriginalPopulation;
        private int m_RefugeesAtBorder;
        private bool m_RefugeeParkBuiltSent;
        private double m_LastNagGameHour;
        private double m_LastUpdateTime = -1.0;
        private bool m_ShownRefugeeModal;
        private bool m_ShownCollapseModal;
        private int m_PendingRefugeeUnits;
        // Park-anchored influx target (persisted via the keyed serialization block,
        // order-independent — not part of the positional codec above).
        private bool m_ParkAnchored;
        private int m_TargetRefugees;
        private double m_AnchorGameHour;

        // ===== Entity Queries =====
        private EntityQuery m_ParkQuery;
        private EntityQuery m_OutsideConnectionQuery;
        private EntityQuery m_MilestoneQuery;


        // Cache citizen count for rate/cap calculation (recompute every 24 hours to avoid sync-point query every hourly tick)
        private const int RATE_CACHE_HOURS = 24;
        [System.NonSerialized] private float m_CachedCitizenCount;
        [System.NonSerialized] private double m_LastRateRecomputeGameHours = double.NegativeInfinity;

        // S15-1 FIX: Resync timer on first frame after load to prevent catch-up burst
        private bool m_NeedsTimeResync;
        [System.NonSerialized] private bool m_PostedRefugeeMessageThisUpdate;

        // One-shot diagnostic dump of the household prefab pool (per process session)
        [System.NonSerialized] private bool m_PrefabPoolLogged;

        // ===== Services (initialized in OnCreate) =====
        private RefugeeSpawnService? m_SpawnService;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        private GameSimulationEndBarrier m_ECBSystem = null!;
        private GameTimeSystem? m_TimeProvider;  // SR-011 FIX: Cache service
        [System.NonSerialized] private RefugeeInfluxCoordinator? m_Coordinator;
        [System.NonSerialized] private bool m_HasRestoredSpawnServiceRandomState;
        [System.NonSerialized] private uint m_RestoredSpawnServiceRandomState;
        private readonly List<RefugeeSpawnService.HouseholdPrefabChoice> m_HouseholdPrefabChoices = new();

        // ===== UI Bindings (initialized in OnCreate) =====
        private ProfiledBinding<int> m_RefugeesReceivedBinding = null!;
        private ProfiledBinding<int> m_HoursRemainingBinding = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Active = false;
            m_HoursElapsed = 0;
            m_TotalRefugeesAdded = 0;
            m_SpawnCounter = 0;
            m_RefugeesAtBorder = 0;

            // Create entity queries for spawn point selection
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

            m_MilestoneQuery = GetEntityQuery(
                ComponentType.ReadOnly<MilestoneLevel>()
            );

            // Create UI bindings
            m_RefugeesReceivedBinding = new ProfiledBinding<int>(B.Group, B.RefugeesReceived, 0);
            m_HoursRemainingBinding = new ProfiledBinding<int>(B.Group, B.RefugeeHoursRemaining, 0);

            AddBinding(m_RefugeesReceivedBinding.Binding);
            AddBinding(m_HoursRemainingBinding.Binding);

            // Dismiss triggers. Lifecycle wrap: modal dismiss touches saved modal state.
            AddBinding(new TriggerBinding(B.Group, B.DismissRefugeeModal,
                CivicGameLifecycle.GameplayOnly(DismissRefugeeModal)));
            AddBinding(new TriggerBinding(B.Group, B.DismissCollapseModal,
                CivicGameLifecycle.GameplayOnly(DismissCollapseModal)));

            // Initialize ECB system for deferred entity creation
            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // SR-011 FIX: Cache time provider in OnCreate
            m_TimeProvider = GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                Log.Warn("GameTimeSystem not available — timing may be inaccurate");
            }

            m_Coordinator = World.GetExistingSystemManaged<RefugeeInfluxCoordinator>();
            m_DependencyWire = new CivicDependencyWire(nameof(RefugeeSpawnSystem));
            SubscribeRequired<ModalActivatedEvent>(OnModalActivated);

            // RefugeeSpawnService is [OwnedByFeatureId(Refugees)], so ServiceRegistry only
            // accepts its registration while the feature registration scope is active —
            // and that scope is open during OnCreate (RegisterSystems), not OnStartRunning.
            // Resolve (create + register) here; Initialize() (queries + RNG seed) stays in
            // OnStartRunning where GameTimeSystem is ready.
            m_SpawnService = m_DependencyWire.RequireWired(ResolveSpawnService);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Service was created and registered in OnCreate (inside the feature scope).
            // Initialize() is idempotent; safe on every enable. RNG restore is one-shot.
            m_SpawnService!.Initialize();
            if (m_HasRestoredSpawnServiceRandomState)
            {
                m_SpawnService.RestoreRandomState(m_RestoredSpawnServiceRandomState);
                m_HasRestoredSpawnServiceRandomState = false;
                m_RestoredSpawnServiceRandomState = 0u;
            }
        }

        private RefugeeSpawnService ResolveSpawnService()
        {
            var spawnService = ServiceRegistry.TryGet<RefugeeSpawnService>();
            if (spawnService == null)
            {
                spawnService = new RefugeeSpawnService(World);
                if (ServiceRegistry.IsInitialized)
                    ServiceRegistry.Instance.Register(spawnService);
                Log.Info("RefugeeSpawnService created and registered");
            }
            else
            {
                Log.Info("RefugeeSpawnService already registered, reusing");
            }

            return spawnService;
        }
        /// <summary>
        /// Start refugee influx after war begins (Village scenario).
        /// Called by OminousSignsSystem or IntroScenarioSystem.
        /// </summary>
        /// <summary>
        /// Resume refugee influx after load without zeroing deserialized progress.
        /// Only re-syncs time reference and sets active flag.
        /// </summary>
        public void ResumeRefugeeInflux()
        {
            m_Active = true;
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider != null)
                m_LastUpdateTime = m_TimeProvider.Current.TotalGameHours;
            Log.Info($"=== REFUGEE INFLUX RESUMED === Hours={m_HoursElapsed:F1}, Total={m_TotalRefugeesAdded}");
        }

        public void StartRefugeeInflux()
        {
            if (m_Active)
            {
                Log.Warn("Already active, ignoring StartRefugeeInflux");
                return;
            }

            m_Active = true;
            m_HoursElapsed = 0;
            m_TotalRefugeesAdded = 0;
            m_OriginalPopulation = this.GetCitizenCount();
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null) { Log.Error("[RefugeeSpawnSystem] TimeProvider unavailable"); return; }
            m_LastUpdateTime = m_TimeProvider.Current.TotalGameHours;
            m_ShownRefugeeModal = false;
            m_ShownCollapseModal = false;
            m_RefugeeParkBuiltSent = false;
            m_ParkAnchored = false;
            m_TargetRefugees = 0;
            m_AnchorGameHour = 0.0;

            Log.Info($"=== REFUGEE INFLUX STARTED === Original pop: {m_OriginalPopulation}");

            // Show initial modal after delay
            ShowRefugeeModal();

            // Post first Chirper message
            PostRefugeeMessage();
        }

        protected override void OnUpdateImpl()
        {
            if (!m_Active)
                return;

            // Retry showing refugee modal if coordinator was busy on first attempt
            if (!m_ShownRefugeeModal)
                ShowRefugeeModal();

            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null) { Log.Error("[RefugeeSpawnSystem] TimeProvider unavailable"); return; }
            double currentGameHours = m_TimeProvider.Current.TotalGameHours;

            if (m_NeedsTimeResync)
            {
                m_NeedsTimeResync = false;
                if (double.IsNaN(m_LastUpdateTime) || double.IsInfinity(m_LastUpdateTime) || currentGameHours < m_LastUpdateTime)
                {
                    m_LastUpdateTime = currentGameHours;
                    Log.Info($"Post-load time anchor corrected: m_LastUpdateTime -> {currentGameHours:F1}h");
                    return;
                }
            }

            if (double.IsNaN(m_LastUpdateTime) || double.IsInfinity(m_LastUpdateTime) || m_LastUpdateTime < 0.0 || currentGameHours < m_LastUpdateTime)
            {
                m_LastUpdateTime = currentGameHours;
                return;
            }

            // Check milestone requirement (Large Village = 3). While locked, keep the
            // anchor current so unlocking cannot replay hours that were never eligible.
            if (!CanSpawnRefugees())
            {
                m_LastUpdateTime = currentGameHours;
                return;
            }

            var scenarioCfg = BalanceConfig.Current.Scenario;

            // First park built — anchor the influx target: +RefugeeTargetPercent of
            // the population at this moment, delivered over RefugeeInfluxDurationHours.
            // Pre-park border trickle counts toward the same target.
            if (!m_ParkAnchored && !m_ParkQuery.IsEmptyIgnoreFilter)
            {
                int population = this.GetCitizenCount();
                m_ParkAnchored = true;
                m_AnchorGameHour = currentGameHours;
                m_TargetRefugees = math.max(0, (int)math.round(population * scenarioCfg.RefugeeTargetPercent));
                Log.Info($"=== REFUGEE TARGET ANCHORED === pop={population}, target=+{m_TargetRefugees} over {scenarioCfg.RefugeeInfluxDurationHours}h (already arrived: {m_TotalRefugeesAdded})");
            }

            // Influx completes when the anchored target is reached. War start ends it
            // earlier via EndInflux (RefugeeInfluxCoordinator on PreWar act exit).
            if (IsTargetReached)
            {
                CompleteInflux();
                return;
            }

            double elapsed = currentGameHours - m_LastUpdateTime;

            if (elapsed >= 1f)
            {
                // Process all missed hours (catch-up loop for lag spikes / fast game speed)
                int hoursToProcess = (int)elapsed;
                const int MAX_CATCHUP_HOURS = 8;
                if (hoursToProcess > MAX_CATCHUP_HOURS)
                {
                    Log.Warn($"Time gap of {elapsed:F1}h — capping catch-up to {MAX_CATCHUP_HOURS}h");
                    hoursToProcess = MAX_CATCHUP_HOURS;
                }
                m_PostedRefugeeMessageThisUpdate = false;
                int processedHours = 0;
                for (int i = 0; i < hoursToProcess; i++)
                {
                    if (IsTargetReached)
                        break;
                    double processedGameHour = m_LastUpdateTime + processedHours + 1.0;
                    ProcessHourlyInflux(processedGameHour);
                    processedHours++;
                }
                if (processedHours > 0)
                    m_LastUpdateTime = Math.Min(currentGameHours, m_LastUpdateTime + processedHours);
            }

            // Check for nag messages when refugees at border without parks
            CheckNagMessages();

            // PropertySeeker disable is owned by RefugeeProcessSystem, which is
            // ordered between HouseholdInitializeSystem and HouseholdFindPropertySystem
            // and skips entirely when no household has PendingRefugeeProcess enabled.

            // Update UI bindings. Before the anchor the window has not started — show
            // the full duration; after it, the hours left until the window closes.
            int influxDurationHours = scenarioCfg.RefugeeInfluxDurationHours;
            int hoursRemaining = m_ParkAnchored
                ? (int)math.ceil(math.max(0.0, m_AnchorGameHour + influxDurationHours - currentGameHours))
                : influxDurationHours;
            m_RefugeesReceivedBinding.Update(m_TotalRefugeesAdded);
            m_HoursRemainingBinding.Update(hoursRemaining);
        }

        /// <summary>
        /// Check if city has reached required milestone for refugee spawning.
        /// </summary>
        private bool CanSpawnRefugees()
        {
            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_MilestoneQuery.TryGetSingleton<MilestoneLevel>(out var milestone))
                return false;

            return milestone.m_AchievedMilestone >= BalanceConfig.Current.Scenario.RefugeeStartMilestone;
        }

        /// <summary>
        /// Get refugee influx rate.
        /// Phase B (park anchored): target-driven — the remaining refugees are spread
        /// across the rest of the influx window. Self-correcting: stalled hours (no
        /// spawn point, lag) shrink the window remainder and raise the rate.
        /// Phase A (no park yet): population-tier trickle scaled by the act multiplier
        /// from RefugeeInfluxCoordinator and capped by RefugeeGrowthCap (prevents a
        /// PeakPopulation spike that makes the population-ratio victory impossible).
        /// </summary>
        private int GetRefugeeRatePerHour(double currentGameHours)
        {
            var scenarioCfg = BalanceConfig.Current.Scenario;

            if (m_ParkAnchored)
            {
                int remaining = m_TargetRefugees - m_TotalRefugeesAdded;
                if (remaining <= 0)
                    return 0;

                double windowEnd = m_AnchorGameHour + scenarioCfg.RefugeeInfluxDurationHours;
                double hoursLeft = math.max(1.0, windowEnd - currentGameHours);
                return (int)math.ceil(remaining / hoursLeft);
            }

            // Recompute citizen count at most once per RATE_CACHE_HOURS game hours — avoids sync-point CalculateEntityCount every hourly tick.
            // Population tier (Village/Town/City) and growth cap change slowly; stale-by-24h is negligible.
            if (double.IsNaN(m_LastRateRecomputeGameHours)
                || double.IsInfinity(m_LastRateRecomputeGameHours)
                || currentGameHours < m_LastRateRecomputeGameHours
                || currentGameHours - m_LastRateRecomputeGameHours >= RATE_CACHE_HOURS)
            {
                m_CachedCitizenCount = this.GetCitizenCount();
                m_LastRateRecomputeGameHours = currentGameHours;
            }
            float population = m_CachedCitizenCount;

            // Population bucketing shared with the Attention exodus city-size multiplier —
            // same Village/Town/City thresholds, classified once in Core so the two cannot
            // drift. Per-tier RATE values remain Refugees-domain (Attention maps the same
            // tier to its own exodus multipliers).
            int baseRate;
            switch (StabilityMath.ClassifyPopulationTier(population))
            {
                case PopulationTier.Village:
                    baseRate = scenarioCfg.RefugeeRateVillage;  // 500/hr (village → town)
                    break;
                case PopulationTier.Town:
                    baseRate = scenarioCfg.RefugeeRateTown;     // 200/hr (moderate)
                    break;
                default:
                    baseRate = scenarioCfg.RefugeeRateCity;     // 50/hr (partial compensation)
                    break;
            }

            // BUG-R-001 FIX: Apply multiplier from coordinator (2.0x Crisis, 0.5x Adaptation, 0.1x Routine)
            var coordinator = m_Coordinator;
            float multiplier = coordinator?.SpawnRateMultiplier ?? 1f;

            int calculatedRate = (int)Math.Round(baseRate * multiplier);

            // BUG FIX: Cap refugee influx to prevent PeakPopulation spike
            // Without cap: Village 500 → 9,600 refugees → PeakPop 10,100
            // When refugees leave → 500/10,100 = 4.9% < 50% victory requirement → GAME OVER
            // With cap: Village 500 → 96 refugees → PeakPop 596
            // When refugees leave → 500/596 = 83.9% > 50% → VICTORY POSSIBLE
            // NEG-FIX: Guard against negative growth cap
            float growthCap = math.max(0f, scenarioCfg.RefugeeGrowthCap);
            int maxRefugeesPerHour = (int)Math.Round(population * growthCap);

            return math.min(calculatedRate, math.max(0, maxRefugeesPerHour));
        }

        /// <summary>
        /// Get spawn point for refugees (50/50 split between border and park).
        /// Returns Entity.Null if no valid spawn point exists.
        /// BUG-R-010 FIX: Use random selection instead of hardcoded [0].
        /// </summary>
        [CompletesDependency("GetRefugeeSpawnPoint: throttled spawn-tick path; ToEntityArray materialises Park/OutsideConnection entities for random-index pick (no SystemAPI.Query equivalent for random access)")]
        private Entity GetRefugeeSpawnPoint(out bool isBorderSpawn)
        {
            var parks = m_ParkQuery.ToEntityArray(Allocator.Temp);
            var connections = m_OutsideConnectionQuery.ToEntityArray(Allocator.Temp);

            bool hasParks = parks.Length > 0;
            bool hasConnections = connections.Length > 0;

            Entity result = Entity.Null;
            isBorderSpawn = false;

            // Session-unique seed: spawn counter XOR'd with object hash and TickCount for variety across games and save/loads
            var random = new Unity.Mathematics.Random(
                (uint)((m_SpawnCounter + 1) ^ (World.GetHashCode() * 0x45D9F3B) ^ (uint)System.Environment.TickCount) | 1u);

            if (hasParks && hasConnections)
            {
                // 50/50 split - some at border, some in park
                bool spawnAtBorder = (m_SpawnCounter % 2 == 0);
                if (spawnAtBorder)
                {
                    int connIndex = random.NextInt(0, connections.Length);
                    result = connections[connIndex];
                    isBorderSpawn = true;
                }
                else
                {
                    int parkIndex = random.NextInt(0, parks.Length);
                    result = parks[parkIndex];
                    isBorderSpawn = false;
                }
            }
            else if (hasParks)
            {
                int parkIndex = random.NextInt(0, parks.Length);
                result = parks[parkIndex];
                isBorderSpawn = false;
            }
            else if (hasConnections)
            {
                int connIndex = random.NextInt(0, connections.Length);
                result = connections[connIndex];
                isBorderSpawn = true;
            }

            if (parks.IsCreated) parks.Dispose();
            if (connections.IsCreated) connections.Dispose();
            m_SpawnCounter++;

            return result;
        }

        /// <summary>
        /// Check and post nag messages when refugees are at border without parks.
        /// </summary>
        private void CheckNagMessages()
        {
            if (m_RefugeesAtBorder <= 0) return;

            // Parks exist: post the positive message once, and never nag — the border
            // keeps refilling by design (50/50 spawn split), so border count alone
            // must not route into the no-park branch below.
            if (!m_ParkQuery.IsEmptyIgnoreFilter)
            {
                if (!m_RefugeeParkBuiltSent)
                {
                    EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.RefugeeParkBuilt.ToKey()), "RefugeeSpawnSystem");
                    Log.Info($"Park built! {m_RefugeesAtBorder} refugees can migrate");
                    m_RefugeeParkBuiltSent = true;
                    // This counter drives only the no-park nag flow; migration itself is entity-state based.
                    m_RefugeesAtBorder = 0;
                }
                return;
            }

            // All parks demolished after the positive message — allow it to fire again
            // when the player rebuilds one.
            m_RefugeeParkBuiltSent = false;

            // No parks - nag the player (based on game time)
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null) { Log.Error("[RefugeeSpawnSystem] TimeProvider unavailable"); return; }
            float currentGameHour = m_TimeProvider.Current.TotalGameHours;
            var scenarioCfg2 = BalanceConfig.Current.Scenario;
            if (currentGameHour - m_LastNagGameHour >= scenarioCfg2.NagIntervalHours)
            {
                m_LastNagGameHour = currentGameHour;
                EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.RefugeeNoPark.ToKey()), "RefugeeSpawnSystem");
                Log.Debug("No park nag posted");
            }
        }

        /// <summary>
        /// Process hourly refugee influx: spawn refugees, check collapse conditions, post chirps.
        /// BUG-R-018 FIX: Added XML documentation for complex private method.
        /// </summary>
        private void ProcessHourlyInflux(double currentGameHours)
        {
            var scenarioCfg2 = BalanceConfig.Current.Scenario;
            int waveHour = m_HoursElapsed + 1;

            // Add refugees (city-size scaled rate)
            int refugeesToAdd = GetRefugeeRatePerHour(currentGameHours);

            // BUG-R-011 FIX: Cap spawn rate to prevent frame drops (max 200/hr)
            const int MAX_SPAWN_PER_HOUR = 200;
            if (refugeesToAdd > MAX_SPAWN_PER_HOUR)
            {
                Log.Warn($"Capping spawn rate from {refugeesToAdd} to {MAX_SPAWN_PER_HOUR}/hr");
                refugeesToAdd = MAX_SPAWN_PER_HOUR;
            }
            if (refugeesToAdd <= 0)
                return;

            // Get spawn point (50/50 border/park split)
            Entity spawnPoint = GetRefugeeSpawnPoint(out bool isBorderSpawn);
            if (spawnPoint == Entity.Null)
            {
                Log.Warn("No spawn point available for refugees");
                return;
            }

            // Spawn real household entities
            // Each "refugee" = 1 household (which spawns multiple citizens)
            // DIV-ZERO FIX: Guard against misconfigured RefugeesPerHousehold
            int refPerHousehold = math.max(scenarioCfg2.RefugeesPerHousehold, 1);
            m_PendingRefugeeUnits = math.max(0, m_PendingRefugeeUnits + refugeesToAdd);
            int householdsToSpawn = m_PendingRefugeeUnits / refPerHousehold;
            if (householdsToSpawn <= 0)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"Refugee hour deferred: pending={m_PendingRefugeeUnits}, perHousehold={refPerHousehold}");
                return;
            }

            if (m_SpawnService == null)
            {
                Log.Error("SpawnService is null, cannot spawn refugees");
                return;
            }

            if (spawnPoint != Entity.Null)
            {
                // BUG-R-008 FIX: Consistent null-safe pattern (no early return — collapse check below)
                RefreshHouseholdPrefabChoices();
                var ecb = m_ECBSystem.CreateCommandBuffer();
                int spawned = m_SpawnService.SpawnRefugees(
                    householdsToSpawn,
                    isBorderSpawn,
                    spawnPoint,
                    ecb,
                    m_HouseholdPrefabChoices);

                if (spawned <= 0)
                    return;

                // S15-12 FIX: Count refugees AFTER successful spawn, not before spawn-point check.
                // Use actual spawned households × ratio to avoid phantom counter inflation.
                int actualRefugees = spawned * refPerHousehold;
#pragma warning disable CIVIC226 // Session-scoped hour/refugee counters
                m_HoursElapsed = waveHour;
                m_PendingRefugeeUnits = math.max(0, m_PendingRefugeeUnits - actualRefugees);
                m_TotalRefugeesAdded += actualRefugees;
                if (isBorderSpawn)
                    m_RefugeesAtBorder += actualRefugees;
#pragma warning restore CIVIC226

                // Post Chirper message at configured intervals after a real delivery.
                if (!m_PostedRefugeeMessageThisUpdate && waveHour % Engine.Timing.CHIRPER_POST_INTERVAL_HOURS == 0)
                {
                    PostRefugeeMessage();
                    m_PostedRefugeeMessageThisUpdate = true;
                }

                Log.Info($"Hour {waveHour}: +{actualRefugees} refugees {(isBorderSpawn ? "AT BORDER" : "IN PARK")} (total: {m_TotalRefugeesAdded}, border: {m_RefugeesAtBorder})");

                // Notify other systems with actual count
                NotifyRefugeeAdded(actualRefugees);

                if (Log.IsDebugEnabled) Log.Debug($"Queued {spawned} refugee households");
            }

            // Check for infrastructure collapse using population ratio
            // Trigger when refugees exceed original population by 5x factor
            if (!m_ShownCollapseModal && m_OriginalPopulation > 0)
            {
                float ratio = (float)m_TotalRefugeesAdded / m_OriginalPopulation;
                if (ratio >= scenarioCfg2.CollapsePopulationRatio)
                {
                    Log.Info($"Infrastructure collapse! Ratio: {ratio:F1}x original population");
                    ShowCollapseModal();
                }
            }
        }

        private void RefreshHouseholdPrefabChoices()
        {
            m_HouseholdPrefabChoices.Clear();

            bool logPool = !m_PrefabPoolLogged;
            m_PrefabPoolLogged = true;
            var prefabSystem = logPool ? World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>() : null;

            // Same prefab pool as vanilla HouseholdSpawnSystem (HouseholdData minus
            // DynamicHousehold), narrowed to prefabs with a surname pool:
            // LocalizationCount on the prefab = vanilla RandomLocalizationInitializeSystem
            // rolls a RandomLocalizationIndex for the spawned household. Prefabs without
            // it can never resolve a family name — RefugeeProcessSystem's
            // "Refugees: {LastName}" label would permanently degrade to the bare fallback.
            foreach (var (archetypeData, householdData, prefabEntity) in
                SystemAPI.Query<RefRO<ArchetypeData>, RefRO<HouseholdData>>()
                    .WithNone<DynamicHousehold>()
                    .WithEntityAccess())
            {
                bool hasSurnamePool = SystemAPI.HasBuffer<Game.Prefabs.LocalizationCount>(prefabEntity);

                if (logPool && prefabSystem != null)
                {
                    string prefabName = "(unknown)";
                    string locInfo = "none";
                    if (prefabSystem.TryGetPrefab<Game.Prefabs.PrefabBase>(prefabEntity, out var prefabBase) && prefabBase != null)
                    {
                        prefabName = prefabBase.name;
                        if (prefabBase.TryGet<Game.Prefabs.RandomGenderedLocalization>(out var genderedLoc))
                            locInfo = $"gendered(male={genderedLoc.m_MaleID}, female={genderedLoc.m_FemaleID})";
                        else if (prefabBase.TryGet<Game.Prefabs.RandomLocalization>(out var plainLoc))
                            locInfo = $"plain(id={plainLoc.m_LocalizationID})";
                    }
                    var hd = householdData.ValueRO;
                    Log.Info($"[PREFAB-POOL] {prefabName}: adults={hd.m_AdultCount} children={hd.m_ChildCount} elderly={hd.m_ElderCount} students={hd.m_StudentCount} weight={hd.m_Weight} surnames={(hasSurnamePool ? "yes" : "NO")} loc={locInfo}");
                }

                if (!hasSurnamePool)
                    continue;

                m_HouseholdPrefabChoices.Add(new RefugeeSpawnService.HouseholdPrefabChoice(
                    prefabEntity,
                    archetypeData.ValueRO.m_Archetype));
            }
        }

        private void CompleteInflux()
        {
            m_Active = false;
            m_Coordinator?.ClearInfluxActivated();

            Log.Info($"=== INFLUX COMPLETE === Total: {m_TotalRefugeesAdded} refugees");

            if (m_TotalRefugeesAdded <= 0)
            {
                Log.Warn("Refugee influx completed with zero spawned refugees; suppressing completion narrative");
                return;
            }

            // Post completion message
            // BUG-R-014 FIX: Reduce allocations with helper method for single-arg events
            var eventArgs = NarrativeArgs.OneArg(m_TotalRefugeesAdded);
            EventBus?.SafePublish(new NarrativeTriggerEvent(
                NarrativeTrigger.RefugeeComplete.ToKey(),
                eventArgs
            ), "RefugeeSpawnSystem");

            // Post news
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.RefugeeUnNews.ToKey()), "RefugeeSpawnSystem");
        }

#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
        private void ShowRefugeeModal()
        {
            if (m_ShownRefugeeModal)
                return;

            // Only show refugee modal for Village scenario (refugees arrive)
            // For Town/City scenarios, refugees don't flood in - citizens leave instead
            // ECS-Pure: Check scenario type via ScenarioSingleton
            if (!SystemAPI.TryGetSingleton<ScenarioSingleton>(out var scenario)
                || scenario.ScenarioType == ScenarioType.None)
            {
                // Scenario type not committed yet — do NOT latch a refusal on an
                // undetermined type. OnUpdateImpl retries ShowRefugeeModal until the
                // type is fixed, so a None here simply defers to a later tick.
                if (Log.IsDebugEnabled)
                    Log.Debug("Scenario type not yet committed — deferring refugee modal");
                return;
            }

            if (scenario.ScenarioType != ScenarioType.Village)
            {
                Log.Info($"Skipping refugee modal for {scenario.ScenarioType} scenario");
                m_ShownRefugeeModal = true; // Final refusal — type is definitively Town/City
                return;
            }

            if (!ModalCoordinator.Instance.TryShow("Refugee"))
                return;

            Log.Info("Requested refugee arrival modal");
        }

        /// <summary>
        /// Show infrastructure collapse modal and trigger consequences.
        /// BUG-R-013 FIX: Added InfrastructureCollapseEvent for system-wide reaction.
        /// S16-04 ACCEPTED: Happiness-tax feedback loop is intentional cascade punishment.
        /// Collapse → happiness penalty → lower tax → budget failure = designed pressure.
        /// </summary>
        private void ShowCollapseModal()
        {
            if (m_ShownCollapseModal)
                return;

            if (!ModalCoordinator.Instance.TryShow("Collapse"))
                return;

            Log.Info("Requested infrastructure collapse modal");
        }

        private void DismissRefugeeModal()
        {
            ModalCoordinator.Instance.Dismiss("Refugee");
        }

        private void DismissCollapseModal()
        {
            ModalCoordinator.Instance.Dismiss("Collapse");
        }
#pragma warning restore CIVIC098

        private void OnModalActivated(ModalActivatedEvent evt)
        {
            if (evt.Id == "Refugee")
            {
                if (m_ShownRefugeeModal) return;
                m_ShownRefugeeModal = true;
                Log.Info("Showing refugee arrival modal");
            }
            else if (evt.Id == "Collapse")
            {
                if (m_ShownCollapseModal) return;
                m_ShownCollapseModal = true;
                PublishInfrastructureCollapse();
            }
        }

        private void PublishInfrastructureCollapse()
        {
            float ratio = m_OriginalPopulation > 0 ? (float)m_TotalRefugeesAdded / m_OriginalPopulation : 0f;
            var collapseEvent = new InfrastructureCollapseEvent(
                m_TotalRefugeesAdded,
                m_OriginalPopulation,
                ratio
            );
            EventBus?.SafePublish(collapseEvent, "RefugeeSpawnSystem");
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.RefugeeCollapse.ToKey()), "RefugeeSpawnSystem");

            Log.Warn($"INFRASTRUCTURE COLLAPSE: {m_TotalRefugeesAdded} refugees ({ratio:F1}x original population) overwhelming water/sewage systems");
        }

        /// <summary>
        /// Post Chirper message about refugee arrival progress.
        /// BUG-R-018 FIX: Added XML documentation.
        /// </summary>
        private void PostRefugeeMessage()
        {
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.RefugeeArrived.ToKey()), "RefugeeSpawnSystem");
        }

        /// <summary>
        /// Notify other systems about refugees added (hook for water/sewage/power integration).
        /// BUG-R-018 FIX: Added XML documentation.
        /// </summary>
        /// <param name="count">Number of refugees added this update</param>
        private void NotifyRefugeeAdded(int count)
        {
            // CIVIC243 FIX: Publish event for ScenarioStatisticsSystem (RecordRefugeesReceived)
            EventBus?.SafePublish(new RefugeesReceivedEvent(count), "RefugeeSpawnSystem");
        }

        // ===== Public API =====

        /// <summary>Is refugee influx currently active?</summary>
        public bool IsActive => m_Active;

        /// <summary>True once the park-anchored refugee target has been delivered (used by post-load reconcile).</summary>
        public bool IsTargetReached => m_ParkAnchored && m_TotalRefugeesAdded >= m_TargetRefugees;

        /// <summary>
        /// End the influx window externally — the war reached the region and the road
        /// to the village is cut. Idempotent; called by RefugeeInfluxCoordinator when
        /// the PreWar act ends. Quiet mode (post-load reconcile) skips the completion
        /// narrative so loading an old save does not re-fire Chirper messages.
        /// </summary>
        public void EndInflux(string reason, bool quiet = false)
        {
            if (!m_Active)
                return;

            Log.Info($"=== INFLUX ENDED === {reason}");
            if (quiet)
            {
                m_Active = false;
                m_Coordinator?.ClearInfluxActivated();
                return;
            }

            CompleteInflux();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ModalActivatedEvent>(OnModalActivated);

            // BUG-CORE-R10 FIX: Unregister and dispose spawn service on destroy
            if (m_SpawnService != null)
            {
                if (ServiceRegistry.IsInitialized)
                {
                    ServiceRegistry.Instance.Unregister<RefugeeSpawnService>(m_SpawnService);
                }
                m_SpawnService.Dispose();
                m_SpawnService = null;
            }
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
