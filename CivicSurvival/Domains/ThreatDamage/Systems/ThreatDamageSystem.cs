using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Events;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Collections;
using Colossal.Mathematics;
using CivicSurvival.Core.Systems.Scheduling;

// Alias to resolve ambiguity between Game.Buildings.Student and Game.Citizens.Student
using Student = Game.Buildings.Student;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.CameraTracking;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.ThreatDamage.Helpers;
using System.Threading;
using BuildingClassifier = CivicSurvival.Core.Utils.BuildingClassifier;
using BuildingType = CivicSurvival.Core.Utils.BuildingType;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Constants;
using Game.Areas;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Handles damage from threat impacts.
    /// Applies ThreatImpactData previously moved by ThreatDamageIntakeSystem into
    /// this system's queue. If the building cache is not ready, the queue remains
    /// retained for a later frame.
    ///
    /// Integrates with CS2 vanilla systems:
    /// - Fire events (Game.Events.Fire)
    /// - Building destruction (Game.Common.Destroyed)
    /// - VFX effects (explosions, fire, smoke)
    ///
    /// Damage types:
    /// - Direct hit: Building destroyed, area damage, VFX
    /// - Debris: Fire started at random nearby building
    /// - Ballistic: Large radius destruction with VFX chain
    ///
    /// Delegates:
    /// - Debris fall animation → DebrisSystem
    /// - Building damage application → BuildingDamageHelper
    /// - Casualty calculation → CasualtyHelper
    /// - Power plant damage → OperationalDamageSystem
    /// </summary>
    [SingletonOwner(typeof(ImpactPressureSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = false,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [HotPathSystem]
    public partial class ThreatDamageSystem : CivicSystemBase,
        ICivicSingletonOwner<ImpactPressureSingleton>
    {
        // ECB command counter (encapsulated to avoid CA2211)
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters()
        {
            Interlocked.Exchange(ref s_EcbCommandCount, 0);
            Interlocked.Exchange(ref s_DroppedStaleImpactCount, 0);
            Interlocked.Exchange(ref s_DroppedUnstampedImpactCount, 0);
        }
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        // C-5 threat-generation root fix: drop impacts from a stale loaded-world
        // generation or never stamped (0). Fail-loud — a missed stamp / lingering
        // post-load zombie becomes a loud, counted, always-dropped impact, never
        // "sometimes a normal impact". Counters surface on the PERF/debug path so
        // "missiles don't damage" is instantly diagnosable (Axiom 1).
        [System.NonSerialized] private ThreatGenerationClock m_threatGenerationClock = null!;
        private const int DROP_LOG_THROTTLE_FRAMES = 60;
        private static int s_DroppedStaleImpactCount;
        private static int s_DroppedUnstampedImpactCount;
        public static int DroppedStaleImpactCount => Volatile.Read(ref s_DroppedStaleImpactCount);
        public static int DroppedUnstampedImpactCount => Volatile.Read(ref s_DroppedUnstampedImpactCount);

        // ===== Magic number constants =====
        private const float SHAHED_SHAKE_INTENSITY = 1.5f;
        private const float SHAHED_SHAKE_DURATION = 0.4f;
        private const float BALLISTIC_SHAKE_INTENSITY = 4f;
        private const float BALLISTIC_SHAKE_DURATION = 0.8f;
        // Debris from a downed drone ignites a few wild trees where it lands; vanilla
        // FireSimulationSystem spreads the forest fire from that seed (range [min, max]).
        private const int DEBRIS_TREE_IGNITE_MIN = 2;
        private const int DEBRIS_TREE_IGNITE_MAX = 3;
        // Event-driven cache: no frame-based expiry. Rebuild on matching-query structural changes.

        private static readonly LogContext Log = new("ThreatDamageSystem");

        private EntityQuery m_BuildingQuery;
        private EntityQuery m_DebriefingQuery;
        private EntityQuery m_CurrentActQuery;

        // Dependencies (initialized in OnCreate/via injection)
        private GameSimulationEndBarrier m_ECBSystem = null!;
        private VanillaVfxSystem? m_VanillaVfx;
        private OperationalDamageSystem m_OperationalDamageSystem = null!;
        private SerializableRandom m_Random;

        private IDefensePolicyReader m_PolicyReader = null!;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;

        // ComponentLookups for atomic TryGetComponent (avoids TOCTOU race)
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;  // CS2 pattern: cross-frame guard
        private ComponentLookup<HealthProblem> m_HealthProblemLookup; // casualty → vanilla death pipeline (Dead-flag dedup)
        private ComponentLookup<Transform> m_TransformLookup;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;

        // Cross-system Destroy/Ignite dedup. Per-system NativeHashSet guards retired
        // in favour of a shared IFrameMutationDedup so PowerBackup / Corruption /
        // PlantWearSimulation / this system all observe each other's queued intent
        // inside one sim frame. Cross-frame protection via HasComponent<Destroyed/Deleted>
        // checks remains authoritative. Frame-end clear runs in FrameMutationDedupClearSystem.
        private IFrameMutationDedup m_FrameMutationDedup = null!;

        // H3 FIX: Victim dedup — prevents duplicate casualty counters/events for citizens
        // who live in Building A and work in Building B when ballistic destroys both.
        [NonEntityIndex] private NativeHashSet<int> m_ProcessedVictims;

        // Event-driven building cache: invalidates when the matching building set changes
        // (new construction, demolition, same-count replacement). Buildings don't move — no time-based expiry.
        // PERF: Double-buffer pattern — use old cache while new one builds async
        [System.NonSerialized] private readonly BuildingCacheManager m_BuildingCacheManager = new();

        private ComponentLookup<OnFire> m_OnFireLookup;

        // Wild-tree forest ignition from debris: vanilla static search tree + the lookups
        // that classify a quad-tree hit as a wild tree (Tree, no Owner).
        private Game.Objects.SearchSystem m_SearchSystem = null!;
        private ComponentLookup<Game.Objects.Tree> m_TreeLookup;
        private ComponentLookup<Owner> m_OwnerLookup;

        // Ignite same-frame intent tracking handled by the shared IFrameMutationDedup
        // above; no per-system fire HashSet remains.
        private ComponentLookup<ElectricityProducer> m_ElectricityProducerLookup;
        private NativeHashSet<Entity> m_AirDefenseBuildings;
        private EntityQuery m_AirDefenseInstallationQuery;
        [System.NonSerialized] private readonly AirDefenseBuildingFilter m_AirDefenseFilter = new();

        // Building classifier (recreated each processing frame with fresh EntityManager state)
        [System.NonSerialized] private BuildingClassifier m_Classifier;
        [System.NonSerialized] private Act m_LastSeenAct;
        [System.NonSerialized] private bool m_HasSeenAct;

        // CDI-2: Casualty lookups for real population deletion
        private BufferLookup<Renter> m_RenterLookup;
        private BufferLookup<HouseholdCitizen> m_HouseholdCitizenLookup;
        private BufferLookup<Employee> m_EmployeeLookup;
        private BufferLookup<Student> m_StudentLookup;
        private BufferLookup<Patient> m_PatientLookup;
        private BufferLookup<ImpactDistrictEntry> m_ImpactDistrictEntryLookup;
        private ComponentLookup<CompanyData> m_CompanyDataLookup;
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;

        // Reusable list for victims (avoids allocation per casualty report)
        private NativeList<Entity> m_VictimsList;

        // Archetypes for CS2 vanilla system integration
        private EntityArchetype m_DestroyEventArchetype;
        private EntityArchetype m_CasualtyEventArchetype;

        // Lookups for the direct OnFire + BatchesUpdated path used by
        // BuildingDamageHelper.TryApplyModFire. InstalledUpgrade buffer +

        // Current frame's ECB (shared across methods)
        private EntityCommandBuffer m_CurrentFrameECB;
        private NativeList<ThreatImpactData> m_PendingApplyImpacts;

        // Cached service references (avoids ServiceRegistry lookup per impact)
        private CameraTrackingSystem? m_CameraTracking;
        private IThreatAudioService m_AudioService = null!;

        // Hybrid batching: collect reports, flush once per frame
        [System.NonSerialized] private DebriefingBatcher m_DebriefingBatcher;
        private ComponentLookup<DebriefingDamageStats> m_DebriefingStatsLookup;

        // T1-3 FIX: Ballistic casualty aggregation — one CasualtyEvent per impact, not per building
        // Not serialized: frame-scoped, reset to 0 at the start of each ballistic impact
        [System.NonSerialized] private bool m_IsBallisticImpact;
        [System.NonSerialized] private int m_BallisticCasualtyTotal;
        [System.NonSerialized] private CasualtyType m_BallisticWorstType;

        // PERF: Reusable NativeList for damage results (avoids allocation per impact)
        private NativeList<DamageResult> m_DamageResults;

        // Impact pressure: district resolution for mental health pipeline
        private ComponentLookup<CurrentDistrict> m_ImpactDistrictLookup;
        [System.NonSerialized]
        private CivicSingletonHandle<ImpactPressureSingleton> m_ImpactSingleton;

        // Progressive damage for non-PP buildings (persistent via mod entities)
        private CivilianDamageSystem m_CivilianDamageSystem = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            int seed = CalculateSeed();
            m_Random = new SerializableRandom(seed);
            Log.Info($" Created with seed {seed}");

            m_BuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );

            m_DebriefingQuery = GetEntityQuery(
                ComponentType.ReadWrite<DebriefingDamageStats>()
            );

            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            // Cross-system caches resolved in OnStartRunning — independent of RegisterAt order.
            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(ThreatDamageSystem));

            // Initialize ComponentLookups for atomic component access
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);  // CS2 pattern
            m_HealthProblemLookup = GetComponentLookup<HealthProblem>(true);
            m_TransformLookup = GetComponentLookup<Transform>(true);

            // V_REGRESSION Phase 8: Destroy/Ignite same-frame dedup moved to
            // shared IFrameMutationDedup (resolved in OnStartRunning).
            m_ProcessedVictims = new NativeHashSet<int>(128, Allocator.Persistent);

            m_BuildingCacheManager.Initialize(m_BuildingQuery);
            m_AirDefenseInstallationQuery = GetEntityQuery(
                ComponentType.ReadOnly<AirDefenseInstallation>(),
                ComponentType.ReadOnly<Simulate>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
            m_AirDefenseBuildings = new NativeHashSet<Entity>(16, Allocator.Persistent);
            m_AirDefenseFilter.Initialize(m_AirDefenseInstallationQuery);
            m_OnFireLookup = GetComponentLookup<OnFire>(true);
            m_ElectricityProducerLookup = GetComponentLookup<ElectricityProducer>(true);

            // Wild-tree forest ignition (debris): vanilla static object search tree + tree/owner
            // classification lookups.
            m_SearchSystem = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_TreeLookup = GetComponentLookup<Game.Objects.Tree>(true);
            m_OwnerLookup = GetComponentLookup<Owner>(true);

            // CDI-2: Initialize casualty lookups for real population deletion
            m_RenterLookup = GetBufferLookup<Renter>(true);
            m_HouseholdCitizenLookup = GetBufferLookup<HouseholdCitizen>(true);
            m_EmployeeLookup = GetBufferLookup<Employee>(true);
            m_StudentLookup = GetBufferLookup<Student>(true);
            m_PatientLookup = GetBufferLookup<Patient>(true);
            m_ImpactDistrictEntryLookup = GetBufferLookup<ImpactDistrictEntry>(false);
            m_CompanyDataLookup = GetComponentLookup<CompanyData>(true);
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true);
            m_DebriefingStatsLookup = GetComponentLookup<DebriefingDamageStats>(false);

            // Reusable victims list (worst case: hospital with 100 patients)
            m_VictimsList = new NativeList<Entity>(128, Allocator.Persistent);

            // Create archetypes for CS2 vanilla systems.
            // No Ignite archetype: mod fires apply OnFire + BatchesUpdated directly
            // via BuildingDamageHelper. Constructing a journal-less Ignite event
            // would propagate m_Event = Entity.Null into OnFire (decompile
            // IgniteSystem.cs:62), breaking spread/escalation in FireSimulationSystem.
            // Destroy event → DestroySystem → Destroyed + removes ElectricityConsumer/Producer etc.
            m_DestroyEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<Destroy>()
            );
            // CasualtyEvent → WorldShockReactionSystem → attention mechanics
            // Note: Need dummy Event component to satisfy params array requirements in .NET 4.8
            m_CasualtyEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<CasualtyEvent>()
            );

            // No self-registration: accessed via World.GetExistingSystemManaged<ThreatDamageSystem>()

            // Hybrid batching: pre-allocate (64 is enough for worst-case wave)
            m_DebriefingBatcher = DebriefingBatcher.Create(64);

            // PERF: Reusable list for damage results (worst-case: ballistic hits 50 buildings)
            m_DamageResults = new NativeList<DamageResult>(64, Allocator.Persistent);
            m_PendingApplyImpacts = new NativeList<ThreatImpactData>(32, Allocator.Persistent);

            // Impact pressure pipeline
            m_ImpactDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_ImpactSingleton = CreateSingletonHandle<ImpactPressureSingleton>();
            EnsureImpactPressureSingleton(EntityManager);
            PressureRegistry.RegisterProducer(PressureChannel.Impact, nameof(ThreatDamageSystem));

            Log.Info(" Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureImpactPressureSingleton(EntityManager);
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            m_FrameMutationDedup ??= ServiceRegistry.Instance.Require<IFrameMutationDedup>();

            // Cross-system caches — same-domain (Threat Damage). All ThreatDamageDomain
            // systems are open together, so Require throws only on a real registration bug.
            m_OperationalDamageSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<OperationalDamageSystem>());
            m_CivilianDamageSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<CivilianDamageSystem>());
            // C-5: threat-generation clock (process-lifetime; resolved here, never in
            // OnUpdate — CIVIC018-safe). The field read in ProcessAllImpacts is hot-safe.
            m_threatGenerationClock ??= m_DependencyWire.RequireWired(() => ServiceRegistry.Instance.Require<ThreatGenerationClock>());

            if (ServiceRegistry.IsInitialized)
            {
                m_PolicyReader = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDefensePolicyReader.Instance);
            }

            // Self-wire dependencies
            m_VanillaVfx = World.GetExistingSystemManaged<VanillaVfxSystem>();

            // Cache CameraTrackingSystem (avoids repeated World lookup per impact)
            m_CameraTracking = World.GetExistingSystemManaged<CameraTrackingSystem>();

            // CIVIC243 FIX: Wire PlayImpactSound (was dead — only PlayInterceptSound was connected)
            m_AudioService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatAudioService.Instance);

            SeedActBaselineFromSingleton(force: false);

            // Pre-warm building cache async — completes naturally before first impacts arrive
            // (threats need hundreds of frames to fly to target after ActiveThreat appears)
            m_BuildingCacheManager.Prewarm(UnityEngine.Time.frameCount);

            Log.Info(" Self-wired dependencies");
        }

        private void SeedActBaselineFromSingleton(bool force)
        {
            if (!force && m_HasSeenAct)
                return;

            if (!TryReadCurrentAct(out var current))
                return;

            m_LastSeenAct = current;
            m_HasSeenAct = true;
        }

        private void ReconcileActTransition()
        {
            if (!TryReadCurrentAct(out var current))
                return;

            if (!m_HasSeenAct)
            {
                m_LastSeenAct = current;
                m_HasSeenAct = true;
                return;
            }

            if (current == m_LastSeenAct)
                return;

            var previous = m_LastSeenAct;
            m_LastSeenAct = current;
            OnObservedActTransition(previous, current);
        }

        private bool TryReadCurrentAct(out Act current)
        {
            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var singleton))
            {
                current = singleton.CurrentAct;
                return true;
            }

            current = default;
            return false;
        }

        private void OnObservedActTransition(Act previous, Act current)
        {
            _ = previous;

            // Reset: clear frame-local state on act transition.
            // V_REGRESSION Phase 8: shared IFrameMutationDedup cleared every frame
            // by FrameMutationDedupClearSystem — no per-act reset needed.
            if (m_ProcessedVictims.IsCreated)
                m_ProcessedVictims.Clear();
            m_IsBallisticImpact = false;
            m_BallisticCasualtyTotal = 0;
            m_BallisticWorstType = default;
            // Clear stale impact buffer before resetting entity ref (order matters)
            var impactSingletonEntity = m_ImpactSingleton.Entity;
            if (impactSingletonEntity != Entity.Null
                && m_ImpactDistrictEntryLookup.HasBuffer(impactSingletonEntity))
                m_ImpactDistrictEntryLookup[impactSingletonEntity].Clear();
            m_ImpactSingleton.Invalidate();
            m_BuildingCacheManager.ResetForReload();

            Log.Info($"[ThreatDamage] Reset state on act transition → {current}");
        }

        protected override void OnDestroy()
        {
            PressureRegistry.DeregisterProducer(PressureChannel.Impact, nameof(ThreatDamageSystem));

            // Dispose cached building data (including pending async cache)
            m_BuildingCacheManager.DisposeAll();

            // Dispose same-frame tracking. V_REGRESSION Phase 8: frame-mutation
            // dedup is a process-lifetime service owned by Mod — do not dispose.
            if (m_ProcessedVictims.IsCreated) m_ProcessedVictims.Dispose();
            if (m_AirDefenseBuildings.IsCreated) m_AirDefenseBuildings.Dispose();

            // Dispose batching
            if (m_DebriefingBatcher.IsCreated) m_DebriefingBatcher.Dispose();

            // Dispose reusable damage results list
            if (m_DamageResults.IsCreated) m_DamageResults.Dispose();
            if (m_PendingApplyImpacts.IsCreated) m_PendingApplyImpacts.Dispose();


            // CDI-2: Dispose victims list
            if (m_VictimsList.IsCreated) m_VictimsList.Dispose();

            DestroyOwnedSingleton(ref m_ImpactSingleton);

            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // m_PolicyReader initialized in OnStartRunning to null-object → real or NullDefensePolicyReader
            if (m_PolicyReader == null)
            {
                m_PolicyReader = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDefensePolicyReader.Instance);
            }

            m_ImpactDistrictEntryLookup.Update(this);
            ReconcileActTransition();

            // Per-frame state reset — MUST run every frame regardless of impacts.
            // m_DeletionQueued / m_InFrameEcbHits are "cleared by caller each frame" contract.
            m_DebriefingBatcher.Clear();
            m_OperationalDamageSystem.ClearInFrameHits();
            m_CivilianDamageSystem.ClearInFrameHits();

            // PERF FIX #1: Early exit when no impact data (0ms when quiet)
            if (!HasPendingApplyImpacts) return;

            using (Core.Utils.PerformanceProfiler.Measure("ThreatDamage.OnUpdate"))
            {
                // PERF FIX #2: Update lookups only when processing impacts (inside profiling scope)
                using (Core.Utils.PerformanceProfiler.Measure("SP:TDS.LookupSync"))
                {
                    m_DestroyedLookup.Update(this);
                    m_DeletedLookup.Update(this);  // CS2 pattern
                    m_HealthProblemLookup.Update(this);  // casualty death-pipeline dedup
                    m_TransformLookup.Update(this);
                    m_OnFireLookup.Update(this);
                    m_TreeLookup.Update(this);
                    m_OwnerLookup.Update(this);
                    m_ElectricityProducerLookup.Update(this);

                    // CDI-2: Update casualty lookups
                    m_RenterLookup.Update(this);
                    m_HouseholdCitizenLookup.Update(this);
                    m_EmployeeLookup.Update(this);
                    m_StudentLookup.Update(this);
                    m_PatientLookup.Update(this);
                    m_CompanyDataLookup.Update(this);
                    m_PropertyRenterLookup.Update(this);
                    m_ImpactDistrictLookup.Update(this);
                }

                // CS2 PATTERN: Clear same-frame HashSets every frame.
                // V_REGRESSION Phase 8: shared IFrameMutationDedup frame-cleared
                // by FrameMutationDedupClearSystem (after barrier playback).
                // Cross-frame protection is via HasComponent<Destroyed/Deleted> checks.
                m_ProcessedVictims.Clear();
                m_AirDefenseFilter.Refresh(EntityManager, m_AirDefenseBuildings);

                m_Classifier = new BuildingClassifier(EntityManager);

                if (!TryResolveImpactPressureEntity(out _))
                {
                    Log.Warn("ImpactPressureSingleton missing during update; impact processing deferred");
                    return;
                }

                // Create ECB for this frame (used by ReportCasualties and other methods)
                m_CurrentFrameECB = m_ECBSystem.CreateCommandBuffer();
                var renderTicket = m_RenderWriteBarrier.Consume(GetType(), RenderWriteComponentMask.BuildingTransform);

                // F28 FIX: Build cache once per frame before processing any impacts.
                // Previously called per-impact in ProcessDirectHit/ProcessDebrisImpact/ProcessBallisticImpact,
                // causing redundant sync points (ToEntityArray/ToComponentDataArray each call CompleteDependency).
                if (!m_BuildingCacheManager.EnsureValid(UnityEngine.Time.frameCount))
                {
                    if (Log.IsDebugEnabled)
                        Log.Debug("Building cache is still building; retaining pending impacts for next frame");
                    return;
                }

                ProcessAllImpacts(m_PendingApplyImpacts, renderTicket);
                m_PendingApplyImpacts.Clear();
                m_ECBSystem.AddJobHandleForProducer(Dependency);
            }

            // Flush batched reports through a cached lookup; avoids EntityManager Get/Set sync on the hot path.
            m_DebriefingStatsLookup.Update(this);
            if (m_DebriefingBatcher.HasPendingReports
                && m_DebriefingQuery.TryGetSingletonEntity<DebriefingDamageStats>(out var debriefingEntity)
                && m_DebriefingStatsLookup.TryGetComponent(debriefingEntity, out var debriefingStats))
            {
                var (totalCost, destroyed, onFire, casualties) = m_DebriefingBatcher.GetTotals();
                debriefingStats.DamageCost = AddClamped(debriefingStats.DamageCost, totalCost);
                debriefingStats.BuildingsDestroyed = AddClamped(debriefingStats.BuildingsDestroyed, destroyed);
                debriefingStats.BuildingsOnFire = AddClamped(debriefingStats.BuildingsOnFire, onFire);
                debriefingStats.Casualties = AddClamped(debriefingStats.Casualties, casualties);
                m_DebriefingStatsLookup[debriefingEntity] = debriefingStats;
            }

            // Report any SpatialHash→Positions index overruns the guard skipped this frame.
            DrainSpatialOob();

            // NOTE: BuildingCache is NOT disposed here - it is event-driven and double-buffered.
        }

        // Bound per-session spatial-desync telemetry: a persistent SpatialHash/Positions desync
        // would otherwise emit every frame. The first handful already carry the magnitude.
        private const int SPATIAL_OOB_EMIT_CAP = 16;
        [System.NonSerialized] private int m_SpatialOobEmitsThisSession;

        private void DrainSpatialOob()
        {
            if (!m_BuildingCacheManager.TryDrainDesync(out int count, out int worstIdx, out int positionsLength, out int hashCount))
                return;
            if (m_SpatialOobEmitsThisSession >= SPATIAL_OOB_EMIT_CAP)
                return;
            m_SpatialOobEmitsThisSession++;
            EventBus?.SafePublish(new SpatialIndexOobEvent(worstIdx, positionsLength, hashCount, count), "ThreatDamageSystem");
            Log.Warn($" Spatial hash index out of range: worstIdx={worstIdx} positions={positionsLength} hash={hashCount} count={count} (read skipped, no crash)");
        }

        private static int AddClamped(int value, int delta)
        {
            long sum = (long)value + delta;
            if (sum <= 0) return 0;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            EnsureImpactPressureSingleton(entityManager);
            ReResolveRuntimeRefs();
            SeedActBaselineFromSingleton(force: true);
        }

        private void ReResolveRuntimeRefs()
        {
            if (ServiceRegistry.IsInitialized)
                m_threatGenerationClock = ServiceRegistry.Instance.Require<ThreatGenerationClock>();
        }

        private void EnsureImpactPressureSingleton(EntityManager entityManager)
        {
            EnsureSingleton(
                ref m_ImpactSingleton,
                entityManager,
                default(ImpactPressureSingleton),
                EnsureImpactPressureShape);
        }

        private void DestroyOwnedSingleton<T>(ref CivicSingletonHandle<T> handle)
            where T : unmanaged, IComponentData
        {
            if (handle.IsCreated && !handle.Query.IsEmpty)
                EntityManager.DestroyEntity(handle.Query);
            handle.Invalidate();
        }

        private bool TryResolveImpactPressureEntity(out Entity entity)
        {
            entity = m_ImpactSingleton.Entity;
            if (entity != Entity.Null
                && SystemAPI.HasComponent<ImpactPressureSingleton>(entity)
                && SystemAPI.HasBuffer<ImpactDistrictEntry>(entity))
            {
                return true;
            }

            if (!m_ImpactSingleton.Query.TryGetSingletonEntity<ImpactPressureSingleton>(out entity)
                || !SystemAPI.HasBuffer<ImpactDistrictEntry>(entity))
            {
                m_ImpactSingleton.Invalidate();
                entity = Entity.Null;
                return false;
            }

            return true;
        }

        private static void EnsureImpactPressureShape(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasBuffer<ImpactDistrictEntry>(entity))
                entityManager.AddBuffer<ImpactDistrictEntry>(entity);
        }

        // ============================================================================
        // IMPACT EVENT PROCESSING
        // ============================================================================

        /// <summary>
        /// Process ALL impact data in this frame.
        /// Processes impacts transferred by ThreatDamageIntakeSystem into the
        /// apply queue. The queue is cleared only after the building cache is ready
        /// and processing completes.
        /// No entity creation/destruction — just data iteration.
        /// </summary>
        private void ProcessAllImpacts(NativeList<ThreatImpactData> impacts, RenderWriteTicket renderTicket)
        {
            int total = impacts.Length;

            if (total == 0) return;

            for (int i = 0; i < total; i++)
            {
                var impact = impacts[i];

                // C-5: drop invalid/stale generations BEFORE any damage path
                // (covers all 3 impact types — no bypass).
                if (impact.ThreatGeneration == ThreatGenerationClock.Unstamped)
                {
                    int n = Interlocked.Increment(ref s_DroppedUnstampedImpactCount);
                    if (n % DROP_LOG_THROTTLE_FRAMES == 1) // log 1st then every Nth
                        Log.Warn($"Dropped UNSTAMPED impact (type={impact.Type}) — forgotten stamp or lingering zombie. total unstamped={DroppedUnstampedImpactCount} stale={DroppedStaleImpactCount}");
                    continue;
                }
                if (impact.ThreatGeneration != m_threatGenerationClock.Current)
                {
                    int n = Interlocked.Increment(ref s_DroppedStaleImpactCount);
                    if (n % DROP_LOG_THROTTLE_FRAMES == 1) // log 1st then every Nth
                        Log.Warn($"Dropped STALE impact (generation={impact.ThreatGeneration} current={m_threatGenerationClock.Current} type={impact.Type}) — post-load/reset transient. total stale={DroppedStaleImpactCount} unstamped={DroppedUnstampedImpactCount}");
                    continue;
                }

                switch (impact.Type)
                {
                    case ImpactType.DirectHit:
                        ProcessDirectHit(m_CurrentFrameECB, impact, renderTicket);
                        break;
                    case ImpactType.Ballistic:
                        ProcessBallisticImpact(m_CurrentFrameECB, impact, renderTicket);
                        break;
                    case ImpactType.Debris:
                        ProcessDebrisImpact(impact.Position, renderTicket);
                        break;
                    default:
                        Log.Error($" Unhandled ImpactType: {impact.Type}");
                        break;
                }
            }
        }

        internal bool HasPendingApplyImpacts => m_PendingApplyImpacts.IsCreated && m_PendingApplyImpacts.Length > 0;

        internal void EnqueueApplyImpacts(NativeArray<ThreatImpactData> impacts)
        {
            if (!impacts.IsCreated || impacts.Length == 0)
                return;

            m_PendingApplyImpacts.AddRange(impacts);
        }

        // ============================================================================
        // DEBRIS IMPACT PROCESSING
        // ============================================================================

        /// <summary>
        /// Handle debris impact on ground. Debris from a downed drone may start a fire where
        /// it lands: on a nearby building, and/or on wild trees (a forest seed that vanilla
        /// then spreads). The two are independent — debris falling into a treeline with no
        /// buildings still ignites the forest.
        /// </summary>
        private void ProcessDebrisImpact(float3 position, RenderWriteTicket renderTicket)
        {
            using var _deb = Core.Utils.PerformanceProfiler.Measure("TDS.Debris");

            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Threats;

            // VFX: Dust cloud on impact
            m_VanillaVfx?.RequestExplosion(position, ExplosionType.Debris);

            IgniteDebrisBuilding(position, cfg.DebrisCheckRadius, renderTicket);
            IgniteDebrisTrees(position, cfg.DebrisCheckRadius);
        }

        /// <summary>
        /// Start a fire on one random eligible building within the debris radius (unchanged
        /// behaviour: AA installations are filtered out by <see cref="AirDefenseBuildingFilter"/>).
        /// </summary>
        private void IgniteDebrisBuilding(float3 position, float radius, RenderWriteTicket renderTicket)
        {
            m_BuildingCacheManager.EnsureValid(UnityEngine.Time.frameCount);
            var nearbyBuildings = SpatialQueryHelper.FindBuildingsInRadius(
                position, radius, in m_BuildingCacheManager.Cache);

            try
            {
                if (nearbyBuildings.Length == 0)
                    return;

                Entity victim = m_AirDefenseFilter.PickRandomEligibleVictim(nearbyBuildings, in m_AirDefenseBuildings, ref m_Random);
                if (victim == Entity.Null)
                    return;

                TriggerBuildingFire(victim, position, renderTicket);

                if (Log.IsDebugEnabled) Log.Debug($" Debris fire at {position}");
                EventBus?.SafePublish(new ThreatNarrativeEvent(ThreatNarrativeEventType.DebrisDamage, Position: position), "ThreatDamageSystem");
            }
            finally
            {
                if (nearbyBuildings.IsCreated) nearbyBuildings.Dispose();
            }
        }

        /// <summary>
        /// Ignite 2-3 random wild trees within the debris radius. Vanilla
        /// <c>FireSimulationSystem</c>/<c>FireSpreadCheckJob</c> then spreads the forest fire
        /// from that seed (decompile-verified: tree spread is not gated by the natural-disasters
        /// setting). Only a small seed is lit so one downed drone does not flatten a whole
        /// forest instantly — the spread does the rest.
        /// </summary>
        private void IgniteDebrisTrees(float3 position, float radius)
        {
            // Fence the borrowed static search tree before the synchronous Iterate —
            // SearchSystem mutates it from a worker on static changes; a concurrent read is a
            // native AV (mirrors FireControlCoordinator). Debris is infrequent, so this sync
            // point is negligible.
            var staticTree = m_SearchSystem.GetStaticSearchTree(readOnly: true, out var staticDeps);
            staticDeps.Complete();

            float r = math.max(1f, radius);
            var bounds = new Bounds3(position - new float3(r, r, r), position + new float3(r, r, r));

            var found = new NativeList<Entity>(Allocator.Temp);
            try
            {
                var iterator = TreeCollectorIterator.Create(
                    bounds, new float2(position.x, position.z), r * r,
                    m_TreeLookup, m_OwnerLookup, found);
                staticTree.Iterate(ref iterator);

                if (found.Length == 0)
                    return;

                int want = math.min(m_Random.Next(DEBRIS_TREE_IGNITE_MIN, DEBRIS_TREE_IGNITE_MAX + 1), found.Length);
                for (int i = 0; i < want; i++)
                {
                    // Partial Fisher-Yates: pick a distinct random tree each iteration.
                    int pick = m_Random.Next(i, found.Length);
                    (found[i], found[pick]) = (found[pick], found[i]);
                    TriggerTreeFire(found[i]);
                }

                // Info (not Debug): event-rare milestone — fires only when debris lands near a
                // wild forest, a few times per wave at most, so it is cheap at Info and gives
                // permanent visibility of the feature without the FPS cost of global Debug.
                Log.Info($" Debris forest fire: {want} tree(s) lit near {position} (of {found.Length} in radius)");
            }
            finally
            {
                if (found.IsCreated) found.Dispose();
            }
        }

        // ============================================================================
        // DIRECT HIT (Shahed)
        // ============================================================================

        /// <summary>
        /// Process direct hit damage (Shahed impact).
        /// VFX sequence: Explosion → Fire → Smoke
        /// </summary>
        private void ProcessDirectHit(EntityCommandBuffer ecb, ThreatImpactData impact, RenderWriteTicket renderTicket)
        {
            using var _dh = Core.Utils.PerformanceProfiler.Measure("TDS.DirectHit");

            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Threats;

            // FIX: Resolve target building from Position (Entity field removed to prevent orphan detection)
            m_BuildingCacheManager.EnsureValid(UnityEngine.Time.frameCount);
            Entity target = SpatialQueryHelper.FindClosestBuilding(
                impact.Position, cfg.DirectHitRadiusBase, in m_BuildingCacheManager.Cache);
            bool isGroundHit = target == Entity.Null;

            // VFX SEQUENCE
            m_VanillaVfx?.RequestExplosion(impact.Position, ExplosionType.DirectHit);

            // Camera shake on impact
            m_CameraTracking?.TriggerShake(
                intensity: SHAHED_SHAKE_INTENSITY * impact.Severity,
                duration: SHAHED_SHAKE_DURATION,
                worldPosition: impact.Position
            );

            // CIVIC243 FIX: Impact sound effect (Shahed = not ballistic)
            m_AudioService.PlayImpactSound(impact.Position, isBallistic: false);

            // Process primary target (only if valid entity)
            bool isPrimaryPP = false;
            if (!isGroundHit)
            {
                using (Core.Utils.PerformanceProfiler.Measure("TDS.PrimaryTarget"))
                {
                    isPrimaryPP = m_ElectricityProducerLookup.HasComponent(target);
                    bool alreadyDead = m_DestroyedLookup.HasComponent(target) || m_DeletedLookup.HasComponent(target);
                    if (Log.IsDebugEnabled) Log.Debug($"[IMPACT] pos=({impact.Position.x:F0},{impact.Position.z:F0}) primary={target.Index} isPP={isPrimaryPP} alreadyDead={alreadyDead}");
                    ProcessPrimaryTarget(ecb, target, impact.Position, impact.ThreatGeneration, renderTicket);
                }
            }
            else
            {
                if (Log.IsDebugEnabled) Log.Debug($"[IMPACT] pos=({impact.Position.x:F0},{impact.Position.z:F0}) primary=GROUND");
            }

            // Area damage to nearby buildings
            float damageRadius = cfg.DirectHitRadiusBase * impact.Severity;
            NativeList<Entity> nearbyBuildings = SpatialQueryHelper.FindBuildingsInRadius(
                impact.Position, damageRadius, in m_BuildingCacheManager.Cache);

            // PERF: Reuse class-level NativeList (avoids allocation per impact)
            m_DamageResults.Clear();
            try
            {
                EmitImpactPressure(target, impact.Severity, nearbyBuildings);

                DamageHelper.CalculateAreaDamage(
                    impact.Position, damageRadius, impact.Severity,
                    nearbyBuildings, m_TransformLookup,
                    m_DeletedLookup, m_DestroyedLookup,
                    renderTicket,
                    target,  // Resolved from Position
                    float.MaxValue,  // shahed area: fire only, no structural collapse of neighbors
                    cfg.FireSeverity,
                    ref m_DamageResults);

                // Log each area damage result
                int areaDestroyed = 0, areaFired = 0;
                foreach (var r in m_DamageResults)
                {
                    bool rIsPP = m_ElectricityProducerLookup.HasComponent(r.Building);
                    if (Log.IsDebugEnabled) Log.Debug($"[AREA] building={r.Building.Index} type={r.Type} severity={r.Severity:F2} isPP={rIsPP}");
                    if (r.Type == DamageType.Destroy) areaDestroyed++;
                    else areaFired++;
                }
                string primaryResult;
                if (isGroundHit) primaryResult = "ground";
                else if (isPrimaryPP) primaryResult = "PP";
                else primaryResult = "CDS";
                if (Log.IsDebugEnabled) Log.Debug($"[IMPACT] summary: primary={primaryResult}, area: radius={damageRadius:F0}m, nearby={nearbyBuildings.Length}, destroyed={areaDestroyed}, fired={areaFired}");

                ApplyDamageResults(ecb, m_DamageResults, impact.Position, impact.ThreatGeneration, renderTicket);
            }
            finally
            {
                if (nearbyBuildings.IsCreated) nearbyBuildings.Dispose();
            }
        }

        /// <summary>
        /// Process primary target hit (power plants get operational damage).
        /// Target is resolved from Position via spatial query.
        /// </summary>
        private void ProcessPrimaryTarget(EntityCommandBuffer ecb, Entity target, float3 impactPosition, int impactThreatGeneration, RenderWriteTicket renderTicket)
        {
            // CS2 PATTERN: Skip already destroyed buildings
            // Check component markers (cross-frame) AND HashSet (same-frame)
            bool isDestroyed = m_DestroyedLookup.HasComponent(target);
            bool isDeleted = m_DeletedLookup.HasComponent(target);
            bool destroyQueued = (m_FrameMutationDedup.GetQueuedKind(target.Index)
                & FrameMutationKind.Destroy) != 0;
            // M6 FIX: Power plants skip destroy-queued guard — ODS tracks in-frame ECB hits internally.
            // Non-PP buildings still need dedup (can't destroy twice).
            bool isPowerPlant = m_ElectricityProducerLookup.HasComponent(target);
            if (isDestroyed || isDeleted || (destroyQueued && !isPowerPlant))
            {
                if (Log.IsDebugEnabled) Log.Debug($"[PPT] SKIP target={target.Index} destroyed={isDestroyed} deleted={isDeleted} destroyQueued={destroyQueued}");
                return;
            }

            bool shouldDestroy = true;

            if (isPowerPlant)
            {
                // P0-200: CalculateDamage handles mod entity creation/update internally
                var result = m_OperationalDamageSystem.CalculateDamage(target, ecb, impactThreatGeneration);

                if (!result.IsValid)
                {
                    // IsValid=false: either not a power plant (ShouldDestroy=true)
                    // or system disabled/act-transition (ShouldDestroy=false — skip damage)
                    shouldDestroy = result.ShouldDestroy;
                }
                else if (result.ShouldDestroy)
                {
                    // Critically damaged - destroy building
                    shouldDestroy = true;
                }
                else
                {
                    shouldDestroy = false;

                    // Start fire if heavily damaged (inline check — result has up-to-date damage
                    // even for in-frame ECB hits that ShouldCatchFire can't see via m_DamageByBuilding)
                    if (result.Damage.DamagePercent >= BalanceConfig.Current.Repair.FireThreshold)
                    {
                        TriggerBuildingFire(target, impactPosition, renderTicket);
                    }
                }
            }
            else
            {
                // Progressive damage via CivilianDamageSystem (persistent across save/load)
                var civResult = m_CivilianDamageSystem.RecordHit(target, ecb, impactThreatGeneration);
                if (!civResult.IsValid)
                {
                    shouldDestroy = false;
                }
                else if (civResult.ShouldDestroy)
                {
                    shouldDestroy = true;
                }
                else
                {
                    shouldDestroy = false;
                    TriggerBuildingFire(target, impactPosition, renderTicket);
                }
            }

            // Destroy primary target if critically damaged
            if (Log.IsDebugEnabled) Log.Debug($"[PPT] target={target.Index} isPowerPlant={isPowerPlant} shouldDestroy={shouldDestroy}");
            if (shouldDestroy)
            {
                DestroyBuilding(ecb, target, isPowerPlant, renderTicket);
            }
        }


        // ============================================================================
        // BALLISTIC IMPACT
        // ============================================================================

        /// <summary>
        /// Process area damage from ballistic missile.
        /// Larger radius, more destruction.
        /// </summary>
        private void ProcessBallisticImpact(EntityCommandBuffer ecb, ThreatImpactData impact, RenderWriteTicket renderTicket)
        {
            using var _bal = Core.Utils.PerformanceProfiler.Measure("TDS.Ballistic");

            // PERF: Cache config locally (avoid 4-level indirection per access)
            var cfg = BalanceConfig.Current.Threats;

            if (Log.IsDebugEnabled) Log.Debug($" Ballistic at {impact.Position}");

            // VFX SEQUENCE (massive)
            m_VanillaVfx?.RequestExplosion(impact.Position, ExplosionType.Ballistic);

            // Heavy camera shake for ballistic impact
            m_CameraTracking?.TriggerShake(
                intensity: BALLISTIC_SHAKE_INTENSITY * impact.Severity,
                duration: BALLISTIC_SHAKE_DURATION,
                worldPosition: impact.Position
            );

            // CIVIC243 FIX: Impact sound effect (ballistic = true)
            m_AudioService.PlayImpactSound(impact.Position, isBallistic: true);

            // T1-3 FIX: Aggregate casualties across all buildings in one ballistic impact
            m_IsBallisticImpact = true;
            m_BallisticCasualtyTotal = 0;
            m_BallisticWorstType = CasualtyType.Residential;

            m_BuildingCacheManager.EnsureValid(UnityEngine.Time.frameCount);
            NativeList<Entity> buildings = SpatialQueryHelper.FindBuildingsInRadius(
                impact.Position, impact.Radius, in m_BuildingCacheManager.Cache);

            // Impact pressure: closest building determines district (matches DirectHit pattern)
            var closestBallistic = SpatialQueryHelper.FindClosestBuilding(
                impact.Position, impact.Radius, in m_BuildingCacheManager.Cache);
            EmitImpactPressure(
                closestBallistic,
                impact.Severity / ThreatConstants.BALLISTIC_IMPACT_SEVERITY,
                buildings);

            // PERF: Reuse class-level NativeList (avoids allocation per impact)
            m_DamageResults.Clear();
            try
            {
                DamageHelper.CalculateAreaDamage(
                    impact.Position, impact.Radius, impact.Severity,
                    buildings, m_TransformLookup,
                    m_DeletedLookup, m_DestroyedLookup,
                    renderTicket,
                    Entity.Null,
                    cfg.BallisticDestructionSeverity,
                    cfg.BallisticFireSeverity,
                    ref m_DamageResults);

                int bDestroyed = 0, bFired = 0;
                foreach (var r in m_DamageResults)
                {
                    if (r.Type == DamageType.Destroy) bDestroyed++;
                    else bFired++;
                }
                if (Log.IsDebugEnabled) Log.Debug($" Ballistic at {impact.Position}: radius={impact.Radius:F0}m, buildings={buildings.Length}, destroyed={bDestroyed}, fired={bFired}");

                ApplyDamageResults(ecb, m_DamageResults, impact.Position, impact.ThreatGeneration, renderTicket);
            }
            finally
            {
                if (buildings.IsCreated) buildings.Dispose();

                // W6-M3: CasualtyEvent in finally — fires even on partial exception in ApplyDamageResults
                if (m_BallisticCasualtyTotal > 0)
                {
                    var eventEntity = m_CurrentFrameECB.CreateEntity(m_CasualtyEventArchetype);
                    IncrementEcbCount();
                    m_CurrentFrameECB.SetComponent(eventEntity, new CasualtyEvent
                    {
                        Count = m_BallisticCasualtyTotal,
                        Type = m_BallisticWorstType,
                        Position = impact.Position
                    });
                    IncrementEcbCount();
                    if (Log.IsDebugEnabled) Log.Debug($" Ballistic total: {m_BallisticCasualtyTotal} casualties (type: {m_BallisticWorstType})");
                }

                m_IsBallisticImpact = false;
            }
        }


        // ============================================================================
        // DAMAGE APPLICATION
        // ============================================================================

        /// <summary>
        /// Apply damage results to buildings (destroy or ignite).
        /// </summary>
        /// <param name="ecb">Command buffer for entity operations</param>
        /// <param name="results">Calculated damage results</param>
        /// <param name="impactPosition">Original impact position (for fire positioning)</param>
        private void ApplyDamageResults(
            EntityCommandBuffer ecb,
            NativeList<DamageResult> results,
            float3 impactPosition,
            int impactThreatGeneration,
            RenderWriteTicket renderTicket)
        {
            using var _apply = Core.Utils.PerformanceProfiler.Measure("TDS.ApplyDamage");
            foreach (var result in results)
            {
                // FIX S2-3: Power plants use progressive operational damage,
                // not instant destroy. Matches ProcessPrimaryTarget behavior.
                if (m_ElectricityProducerLookup.HasComponent(result.Building))
                {
                    // Power plants skip destroy-queued guard — ODS tracks in-frame ECB hits
                    // via m_InFrameEcbHits (same pattern as ProcessPrimaryTarget M6 FIX).
                    var opResult = m_OperationalDamageSystem.CalculateDamage(result.Building, ecb, impactThreatGeneration);
                    if (!opResult.IsValid && !opResult.ShouldDestroy)
                        continue; // System disabled/act-transition — skip damage entirely
                    if (opResult.IsValid && !opResult.ShouldDestroy)
                    {
                        if (opResult.Damage.DamagePercent >= BalanceConfig.Current.Repair.FireThreshold)
                            TriggerBuildingFire(result.Building, impactPosition, renderTicket);
                        continue;
                    }
                    // PP critically damaged or under construction → destroy directly
                    if (opResult.ShouldDestroy)
                    {
                        DestroyBuilding(ecb, result.Building, isPowerPlant: true, renderTicket);
                        continue;
                    }
                }

                if (result.Type == DamageType.Destroy)
                {
                    // Progressive damage for non-PP buildings (ballistic area close)
                    // CivilianDamageSystem owns hit-count accounting. Destroy/fire helpers
                    // dedup side effects independently after the hit is recorded.
                    var civResult = m_CivilianDamageSystem.RecordHit(result.Building, ecb, impactThreatGeneration);

                    if (civResult.IsValid && civResult.ShouldDestroy)
                        DestroyBuilding(ecb, result.Building, isPowerPlant: false, renderTicket);
                    else if (civResult.IsValid)
                    {
                        TriggerBuildingFire(result.Building, impactPosition, renderTicket);
                    }
                }
                else // Fire
                {
                    TriggerBuildingFire(result.Building, impactPosition, renderTicket);
                }
            }
        }

        private void EmitImpactPressure(Entity primary, float severity, NativeList<Entity> fallbackBuildings)
        {
            if (!TryResolveImpactPressureEntity(out var impactSingletonEntity)
                || !SystemAPI.HasBuffer<ImpactDistrictEntry>(impactSingletonEntity))
                return;

            if (TryGetImpactDistrict(primary, out int district))
            {
                SystemAPI.GetBuffer<ImpactDistrictEntry>(impactSingletonEntity).Add(
                    ImpactDistrictEntry.Create(district, severity));
                return;
            }

            for (int i = 0; i < fallbackBuildings.Length; i++)
            {
                if (TryGetImpactDistrict(fallbackBuildings[i], out district))
                {
                    SystemAPI.GetBuffer<ImpactDistrictEntry>(impactSingletonEntity).Add(
                        ImpactDistrictEntry.Create(district, severity));
                    return;
                }
            }

            SystemAPI.GetBuffer<ImpactDistrictEntry>(impactSingletonEntity).Add(
                ImpactDistrictEntry.Create(DistrictUtils.UNZONED_AREA_INDEX, severity));
        }

        private bool TryGetImpactDistrict(Entity building, out int district)
        {
            district = 0;
            if (building == Entity.Null
                || !m_ImpactDistrictLookup.TryGetComponent(building, out var impactDistrict))
                return false;

            district = impactDistrict.m_District.Index;
            return true;
        }

        // ============================================================================
        // BUILDING DAMAGE UTILITIES
        // ============================================================================

        private void DestroyBuilding(EntityCommandBuffer ecb, Entity building, bool isPowerPlant, RenderWriteTicket renderTicket)
        {
            // CS2 NATIVE PATTERN: 3-level idempotency
            // 1. HasComponent<Destroyed/Deleted> - cross-frame guard (in helper)
            // 2. HashSet.Add() - same-frame dedup (in helper)
            // 3. ECB.AddComponent - deferred destruction
            bool isCritical = IsCriticalBuilding(building);
            float3 destructionPosition = BuildingDamageHelper.GetBuildingPosition(building, float3.zero, m_TransformLookup, renderTicket);
            bool destroyed = BuildingDamageHelper.TryDestroyBuilding(
                ecb, building, destructionPosition, m_FrameMutationDedup,
                m_DestroyedLookup, m_DeletedLookup,
                m_DestroyEventArchetype, isCritical, isPowerPlant);

            if (Log.IsDebugEnabled) Log.Debug($"[DB] building={building.Index} tryDestroy={destroyed}");
            if (!destroyed) return; // Already destroyed (cross-frame) or processed (same-frame)

            ReportCasualties(building, destructionPosition);

            // Batch damage report
            int damageCost = EstimateBuildingCost(building);
            m_DebriefingBatcher.ReportDestroyed(damageCost);

            EventBus?.SafePublish(new BuildingDamagedEvent(building.Index, 1), "ThreatDamageSystem");

            if (Log.IsDebugEnabled) Log.Debug($" Building {building.Index} destroyed (critical: {isCritical}), cost: ${damageCost:N0}");
        }

        private bool IsCriticalBuilding(Entity building)
        {
            return m_Classifier.IsCritical(building);
        }

        private void ReportCasualties(Entity building, float3 position)
        {
            using var _cas = Core.Utils.PerformanceProfiler.Measure("TDS.Casualties");
            var buildingType = m_Classifier.Classify(building);

            // Check for scandal (helper encapsulates policy logic).
            // NullDefensePolicyReader returns Unavailable so AirDefense owner absence
            // fails closed for hospital-hit scandal checks.
            if (CasualtyHelper.ShouldTriggerScandal(buildingType, m_PolicyReader.CurrentPolicy))
            {
                EventBus?.SafePublish(new ThreatNarrativeEvent(ThreatNarrativeEventType.HospitalHitScandal, Position: position), "ThreatDamageSystem");
            }

            // Calculate max casualties for this building type
            int maxCasualties = CasualtyHelper.CalculateCasualties(buildingType, ref m_Random, out var casualtyType);
            if (maxCasualties <= 0) return;

            // CDI-2: Get actual citizens to delete from building
            BuildingCasualtyHelper.GetVictims(
                building,
                buildingType,
                maxCasualties,
                ref m_RenterLookup,
                ref m_HouseholdCitizenLookup,
                ref m_EmployeeLookup,
                ref m_StudentLookup,
                ref m_PatientLookup,
                ref m_CompanyDataLookup,
                ref m_PropertyRenterLookup,
                ref m_DeletedLookup,
                in m_ProcessedVictims,
                m_VictimsList);

            // Route casualties through the vanilla death pipeline (deathcare) instead of a direct
            // Deleted. HealthProblem{Dead|RequireTransport} mirrors DeathCheckSystem.Die: vanilla
            // requests a hearse and DeathcareFacilityAISystem applies the structural Deleted later
            // on EndFrameBarrier (MainLoop playback). That keeps the Deleted+UpdateFrame citizen
            // out of the GameSimulation phase entirely — a mod-only divergence that a mis-phased
            // vanilla UpdateGroupSystem turns into a crash — and gives casualties visible aftermath
            // (bodies, hearse demand, deathcare load) instead of a silent population drop.
            int actualCasualties = 0;
            for (int i = 0; i < m_VictimsList.Length; i++)
            {
                Entity victim = m_VictimsList[i];
                // Guard: skip invalid, non-existent, or already-dying entities. A citizen already
                // routed into the death pipeline carries HealthProblem{Dead}; one already removed
                // carries Deleted. Neither lookup sees the ECB-deferred mark this frame, so
                // m_ProcessedVictims (frame-scoped) covers same-frame duplicates.
                // CIVIC051: EntityManager.Exists is the only universal liveness check for citizen
                // entities from stale buffers (Renter/Employee/Student/Patient).
                // H3 FIX: same-frame victim dedup — citizen in Building A (Renter) and Building B
                // (Employee) can be reported by both calls.
#pragma warning disable CIVIC051, CIVIC097, CIVIC436 // Index-only key safe: cleared every frame, no structural changes during ReportCasualties; Exists check on vanilla victim, not owned singleton
                if (victim == Entity.Null || !EntityManager.Exists(victim)
                    || m_DeletedLookup.HasComponent(victim)
                    || IsAlreadyDying(victim)
                    || !m_ProcessedVictims.Add(victim.Index))
#pragma warning restore CIVIC051, CIVIC097, CIVIC436
                    continue;
                m_CurrentFrameECB.AddComponent(victim, new HealthProblem(
                    Entity.Null, HealthProblemFlags.Dead | HealthProblemFlags.RequireTransport));
                IncrementEcbCount();
                actualCasualties++;
            }

            // T1-1 FIX: No phantom casualties — only report real citizen deletions
            if (actualCasualties == 0) return;

            // Batch casualty report (always — debriefing needs per-building counts)
            m_DebriefingBatcher.ReportCasualties(actualCasualties);

            // T1-3 FIX: Ballistic impacts accumulate into one aggregated CasualtyEvent
            if (m_IsBallisticImpact)
            {
#pragma warning disable CIVIC226 // Frame-scoped — reset to 0 in OnUpdateImpl
                m_BallisticCasualtyTotal += actualCasualties;
#pragma warning restore CIVIC226
                // Track worst type: Hospital > School > CriticalInfra > Residential
                if (casualtyType == CasualtyType.Hospital)
                    m_BallisticWorstType = CasualtyType.Hospital;
                else if (casualtyType == CasualtyType.School && m_BallisticWorstType != CasualtyType.Hospital)
                    m_BallisticWorstType = casualtyType;
                else if (casualtyType == CasualtyType.CriticalInfra && m_BallisticWorstType == CasualtyType.Residential)
                    m_BallisticWorstType = casualtyType;
                return;
            }

            // Non-ballistic: one CasualtyEvent per building (DirectHit path)
            var eventEntity = m_CurrentFrameECB.CreateEntity(m_CasualtyEventArchetype);
            IncrementEcbCount();
            m_CurrentFrameECB.SetComponent(eventEntity, new CasualtyEvent
            {
                Count = actualCasualties,
                Type = casualtyType,
                Position = position
            });
            IncrementEcbCount();

            if (Log.IsDebugEnabled) Log.Debug($" {actualCasualties} casualties (routed to deathcare)");
        }

        // A casualty victim already routed into the vanilla death pipeline carries HealthProblem
        // with the Dead flag. Guards against re-reporting the same citizen across impacts/frames
        // before vanilla deathcare applies the structural Deleted (which m_DeletedLookup then
        // catches). m_HealthProblemLookup can't see an ECB-deferred mark added earlier this frame —
        // same-frame duplicates are covered by m_ProcessedVictims.
        private bool IsAlreadyDying(Entity victim)
            => m_HealthProblemLookup.HasComponent(victim)
               && (m_HealthProblemLookup[victim].m_Flags & HealthProblemFlags.Dead) != HealthProblemFlags.None;

        private void TriggerBuildingFire(Entity building, float3 position, RenderWriteTicket renderTicket)
        {
            // TryApplyModFire is the producer half: it creates an event-backed ModFireIntent
            // on the GameSimulation buffer. ModFireApplySystem applies OnFire + BatchesUpdated in
            // ModificationEnd, in phase with the render pass; building damage is then driven by
            // vanilla FireSimulationSystem via the real fire event.
            bool ignited = BuildingDamageHelper.TryApplyModFire(
                m_CurrentFrameECB, building, m_FrameMutationDedup,
                m_OnFireLookup,
                m_DestroyedLookup, m_DeletedLookup);

            if (!ignited) return; // Already on fire, queued for destroy, or invalid

            m_DebriefingBatcher.ReportFire();

            // OnFire + BatchesUpdated drive vanilla building fire VFX directly
            // (BatchDataSystem reads BatchesUpdated; FireSimulationSystem reads OnFire).
            float3 firePosition = BuildingDamageHelper.GetBuildingPosition(building, position, m_TransformLookup, renderTicket);
            if (Log.IsDebugEnabled) Log.Debug($" Building {building.Index} fire applied at {firePosition}");
        }

        /// <summary>
        /// Producer half for a wild-tree fire: creates an event-backed <c>ModFireIntent</c>
        /// tagged <see cref="FireTargetKind.WildTree"/>. <c>ModFireApplySystem</c> backs it with
        /// the vanilla WildTree fire prefab in ModificationEnd; vanilla <c>FireSimulationSystem</c>
        /// then drives burning, spread and destruction off the tree's <c>OnFire</c>. No debriefing
        /// report (forest, not building damage) and no render-ticket position read (trees are not
        /// in the building render-write contract).
        /// </summary>
        private void TriggerTreeFire(Entity tree)
        {
            bool ignited = BuildingDamageHelper.TryApplyModFire(
                m_CurrentFrameECB, tree, m_FrameMutationDedup,
                m_OnFireLookup, m_DestroyedLookup, m_DeletedLookup,
                kind: FireTargetKind.WildTree);

            // Rejected (already burning, queued for destroy, or invalid) → nothing else to do;
            // the next debris hit retries fresh trees.
            if (!ignited && Log.IsDebugEnabled)
                Log.Debug($" Debris tree {tree.Index} fire rejected (already burning/destroying)");
        }

        /// <summary>
        /// Estimate building replacement cost based on type.
        /// </summary>
        private int EstimateBuildingCost(Entity building)
        {
            var buildingType = m_Classifier.Classify(building);
            return BuildingClassifier.GetEstimatedCost(buildingType);
        }

    }
}
