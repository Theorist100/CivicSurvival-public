using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Corruption;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.UI.Toast;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Corruption.Data;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Domain.Power;
using Game.Areas;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// MaintenanceContractSystem: Generates procurement offers for city buildings.
    /// REFACTORED: Pure offer generator. Response handling moved to ContractResponseSystem.
    ///
    /// State stored in ECS:
    /// - PendingProcurement component on building entity (enableable)
    /// - ContractStatsSingleton for aggregated counts
    ///
    /// No longer implements IMaintenanceContractService - interface deleted.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(ContractStatsSingleton))]
    [SingletonOwner(typeof(PendingProcurement))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    public partial class MaintenanceContractSystem : ThrottledSystemBase,
        ICivicSingletonOwner<ContractStatsSingleton>, ICivicSingletonOwner<PendingProcurement>
    {
        protected override string ThrottlePhaseKey => MaintenanceContractReadyMarker.PHASE_KEY;

        private static readonly LogContext Log = new("Procurement");
        // ============================================================================
        // DEPENDENCIES
        // ============================================================================

        private SimulationSystem m_SimulationSystem = null!;
        private PrefabSystem m_PrefabSystem = null!;
        private ToastService? m_ToastService;
        private IShadowReputationService m_ReputationService = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private IDistrictModernizationService m_ModernizationService = null!;
        private IBackupPowerLinkReader m_LinkReader = null!;
        private ComponentLookup<Game.Areas.CurrentDistrict> m_CurrentDistrictLookup;
        private EntityQuery m_PowerPlantQuery;
        private EntityQuery m_PendingOfferQuery;
        private ComponentLookup<PendingProcurement> m_PendingProcurementLookup;
        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private EntityQuery m_PendingOfferAllStatesQuery;

        // PERF: chunk iteration for TryGenerateOffer (avoids SystemAPI.Query dependency tracker leak — 41ms spikes)
        private ComponentTypeHandle<ElectricityProducer> m_ElectricityProducerTypeHandle;

        // PERF: chunk iteration for contract lifecycle (avoids SystemAPI.Query dependency tracker leak)
        private EntityQuery m_ContractIterQuery;
        private ComponentTypeHandle<ContractData> m_ContractDataTypeHandle;
        private EntityTypeHandle m_EntityTypeHandle;

        // PERF: chunk iteration for counterfeit cleanup (avoids SystemAPI.Query dependency tracker leak)
        private EntityQuery m_CounterfeitBatteryQuery;
        private ComponentTypeHandle<CounterfeitBattery> m_CounterfeitBatteryTypeHandle;
        private readonly List<int> m_DmsCleanupDistricts = new();
        private readonly List<long> m_DmsCleanupBuildingKeys = new();

        // Frame-local set of buildings with contracts (ContractData is on separate entities)
        [NonEntityIndex] private NativeHashMap<int, int> m_BuildingsWithContracts;

        // ============================================================================
        // STATE
        // ============================================================================

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;
        private const double PREV_GAME_HOURS_INIT_EPSILON = 0.001;
        private const uint OFFER_SEED_FRAME_MULTIPLIER = 747796405u;
        private const uint OFFER_SEED_DAY_MULTIPLIER = 2891336453u;
        private const uint OFFER_SEED_DAY_BUCKET_SCALE = 1000u;

        private float m_LastOfferGameDay = 0f;
        private double m_PrevGameHours = 0.0;
        [System.NonSerialized] private int m_LastProcurementToastId = -1;
        [System.NonSerialized] private int m_ProcurementToastBuildingIndex = -1;
        [System.NonSerialized] private int m_ProcurementToastBuildingVersion = -1;
        [System.NonSerialized] private bool m_ToastEventsSubscribed;
        [System.NonSerialized] private bool m_ReconcilePendingProcurementAfterLoad;
        private MaintenanceContractUiSnapshot m_UiModel = MaintenanceContractUiSnapshot.Empty;
        private readonly StringBuilder m_ActiveContractsJsonBuilder = new(1024);

        // FIX W2-M6: Defer toast event handling from UIUpdate to GameSimulation phase
        private int m_PendingClearToastId = -1;
        private string? m_PendingClearReason;

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            ContractStatsSingleton.EnsureExists(EntityManager);

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // Query: All power plants (ContractData is on separate entities, filter later)
            m_PowerPlantQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ElectricityProducer>(),
                ComponentType.Exclude<Deleted>()
            );

            // Frame-local set for buildings with contracts
            m_BuildingsWithContracts = new NativeHashMap<int, int>(16, Allocator.Persistent);

            // Query: Buildings with pending offers (for checking if offer exists)
            m_PendingOfferQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<PendingProcurement>(),
                ComponentType.Exclude<Deleted>()
            );

            m_PendingOfferAllStatesQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PendingProcurement>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            m_PendingProcurementLookup = GetComponentLookup<PendingProcurement>(false);
            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(true);
            m_BuildingLookup = GetComponentLookup<Building>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_CurrentDistrictLookup = GetComponentLookup<Game.Areas.CurrentDistrict>(true);

            // PERF: chunk iteration for contract lifecycle (avoids SystemAPI.Query dependency tracker leak)
            m_ContractIterQuery = GetEntityQuery(
                ComponentType.ReadOnly<ContractData>(),
                ComponentType.Exclude<Deleted>()
            );
            m_ContractDataTypeHandle = GetComponentTypeHandle<ContractData>(true);
            m_EntityTypeHandle = GetEntityTypeHandle();
            m_ElectricityProducerTypeHandle = GetComponentTypeHandle<ElectricityProducer>(true);

            // PERF: chunk iteration for counterfeit cleanup (avoids SystemAPI.Query dependency tracker leak)
            m_CounterfeitBatteryQuery = GetEntityQuery(
                ComponentType.ReadOnly<CounterfeitBattery>(),
                ComponentType.Exclude<Deleted>()
            );
            m_CounterfeitBatteryTypeHandle = GetComponentTypeHandle<CounterfeitBattery>(true);

            // FIX W7-H2: Removed RequireForUpdate(m_PowerPlantQuery).
            // MCS is the sole consumer of counterfeit battery cleanup (ClearPendingCounterfeitCleanup)
            // and expired contract cleanup. Counterfeit entities reference consumer buildings (not producers).
            // If all power plants destroyed, MCS must still run cleanup. Offer generation guards on
            // m_PowerPlantQuery.IsEmpty instead.

            // FIX W6-M7: Notification events in OnCreate — always active regardless of power plants.
            // Previously in OnStartRunning → handlers fired while system stopped (no OnStopRunning unsubscribe).
            SubscribeRequired<InvestigationStartedEvent>(OnInvestigationStarted);
            SubscribeRequired<CorruptionNarrativeEvent>(OnCorruptionNarrative);
            SubscribeRequired<InfraEvent>(OnInfraEvent);

            Log.Info(" System created (pure offer generator)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            ContractStatsSingleton.EnsureExists(EntityManager);
            m_LinkReader = ServiceRegistry.Instance.Require<IBackupPowerLinkReader>();
            ResolveFeatureServices();

            // Self-wire toast service (replaces MainUISystem SetToastService injection)
            ResolveToastService();

            Log.Info(" Self-wired ReputationService + ToastService");
        }


        protected override void OnThrottledUpdate()
        {
            ResolveFeatureServices();
            ResolveToastService();

            using (PerformanceProfiler.MeasureDebug("SP:MCS.LookupSync"))
            {
                m_PendingProcurementLookup.Update(this);
                m_PrefabRefLookup.Update(this);
                m_BuildingLookup.Update(this);
                m_DeletedLookup.Update(this);
                m_CurrentDistrictLookup.Update(this);
            }

            ReconcilePendingProcurementAfterLoad();

            // First-update-after-load m_PrevGameHours init is owned solely by the
            // spike guard in TryGenerateOffer; the pending-offer branch below keeps it
            // current while an offer is shown.
            // FIX W2-M6: Process deferred toast clear (set in UIUpdate phase callbacks)
            if (m_PendingClearToastId >= 0)
            {
                ClearPendingOfferByToast(m_PendingClearToastId, m_PendingClearReason ?? "unknown");
                m_PendingClearToastId = -1;
                m_PendingClearReason = null;
            }

            // Update ContractStatsSingleton with current counts
            using (PerformanceProfiler.MeasureDebug("MCS.UpdateStats"))
            {
                UpdateContractStats();
            }

            // Don't generate new offers if one is pending
            // Pending offer cleared automatically via ToastService events or ContractResponseSystem
            if (HasPendingOffer())
            {
                // Keep time tracking current to prevent delta spike when offer clears
                var timeProvider = GameTimeSystem.Instance;
                if (timeProvider != null)
                    m_PrevGameHours = timeProvider.Current.TotalGameHours;
                return;
            }

            // Check if enough time has passed since last offer
            if (!TryGetCurrentGameDay(out var currentGameDay))
                return;
            if (currentGameDay - m_LastOfferGameDay < Core.Config.BalanceConfig.Current.CorruptionEvents.MinDaysBetweenOffers)
                return;

            // Check TrustLevel - frozen players get no offers
            if (m_ReputationService.IsFrozenOut)
            {
                Log.Debug(" Player is frozen out, no offers");
                return;
            }

            // Try to generate an offer
            using (PerformanceProfiler.MeasureDebug("MCS.TryGenerate"))
            {
                TryGenerateOffer(currentGameDay);
            }
        }

        /// <summary>
        /// Check if there's an active pending offer via ECS query.
        /// PERF: IsEmpty respects IEnableableComponent filter — no allocation needed.
        /// </summary>
        private bool HasPendingOffer()
        {
            return !m_PendingOfferQuery.IsEmpty;
        }

        [CompletesDependency("ReconcilePendingProcurementAfterLoad: one-shot post-load reconciliation reads all PendingOffer states (including disabled) for restore; gated by m_ReconcilePendingProcurementAfterLoad flag, idempotent")]
        private void ReconcilePendingProcurementAfterLoad()
        {
            if (!m_ReconcilePendingProcurementAfterLoad)
                return;

            if (m_PendingOfferAllStatesQuery.IsEmptyIgnoreFilter)
            {
                ClearPendingOfferUiModel();
                m_ReconcilePendingProcurementAfterLoad = false; // nothing persisted to restore
                return;
            }

            using var entities = m_PendingOfferAllStatesQuery.ToEntityArray(Allocator.Temp);
            int restored = 0;
            Entity activeEntity = Entity.Null;
            PendingProcurement activePending = default;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
#pragma warning disable CIVIC485 // Reconcile needs component presence; disabled PendingProcurement is the state being restored.
                if (!m_PendingProcurementLookup.HasComponent(entity))
                    continue;
#pragma warning restore CIVIC485

                var pending = m_PendingProcurementLookup[entity];
                if (!IsRecoverablePendingProcurement(entity, pending))
                {
                    ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, entity, "post-load-nonrecoverable");
                    continue;
                }

                if (activeEntity != Entity.Null)
                {
                    ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, entity, "post-load-duplicate-active");
                    continue;
                }

                if (!m_PendingProcurementLookup.IsComponentEnabled(entity))
                {
                    m_PendingProcurementLookup.SetComponentEnabled(entity, true);
                    restored++;
                }

                // Single-pending-offer invariant: the recoverable one is the offer
                // whose transient toast must be rebuilt.
                activeEntity = entity;
                activePending = pending;
            }

            if (activeEntity == Entity.Null)
                ClearPendingOfferUiModel();
            else
                SetPendingOfferUiModel(activeEntity, activePending);

            if (restored > 0)
                Log.Info($"Restored {restored} pending procurement offer(s) after load");

            // Inv 2: the offer is durable (PendingProcurement) but its toast is
            // runtime-only and lost on load, and the toast-id tracking was reset to
            // -1. Without rebuilding the toast the offer stays enabled
            // (HasPendingOffer → no new offers ever) yet invisible and unactionable
            // forever — the "pending offer survives but toast doesn't" dead-lock.
            // Rebuild the transient toast from the durable component. ToastService
            // is registered by the UIUpdate phase and can lag the first sim tick,
            // so retry next tick (keep the flag set) until the toast is queued.
            if (activeEntity == Entity.Null || m_LastProcurementToastId > 0)
            {
                m_ReconcilePendingProcurementAfterLoad = false; // no offer, or toast already tracked
                return;
            }

            if (TryQueueProcurementToast(activeEntity, activePending.Type,
                    activePending.OfficialPrice, activePending.ShadyPrice, activePending.KickbackOffer))
            {
                m_ReconcilePendingProcurementAfterLoad = false;
            }
            // else: ToastService not ready / toast rejected — retry next tick.
        }

        private bool IsRecoverablePendingProcurement(Entity entity, in PendingProcurement pending)
        {
            return pending.Lifecycle == PendingProcurementLifecycle.Active
                && pending.TargetBuilding.Index == entity.Index
                && pending.TargetBuilding.Version == entity.Version
                && m_BuildingLookup.HasComponent(entity)
                && !m_DeletedLookup.HasComponent(entity)
                && !HasLiveContractForBuilding(entity)
                && pending.OfficialPrice > 0
                && pending.ShadyPrice > 0
                && pending.OfficialQuality > 0f
                && pending.ShadyQuality > 0f;
        }

        private bool HasLiveContractForBuilding(Entity buildingEntity)
        {
            bool hasCurrentDay = TryGetCurrentGameDay(out var currentGameDay);
            int currentDay = hasCurrentDay ? (int)currentGameDay : 0;

            foreach (var contractRef in SystemAPI.Query<RefRO<ContractData>>().WithNone<Deleted>())
            {
                var contract = contractRef.ValueRO;
                if (contract.Building.Index != buildingEntity.Index
                    || contract.Building.Version != buildingEntity.Version)
                    continue;

                if (!hasCurrentDay)
                    return true;

                int expirationDay = contract.ContractStartDay + contract.ContractDurationDays;
                if (currentDay < expirationDay)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Update ContractStatsSingleton with current shady/total counts.
        /// Also builds frame-local set of buildings with contracts.
        /// </summary>
        private void UpdateContractStats()
        {
            // Build frame-local set of buildings with contracts
            // ContractData is now on SEPARATE entities (not on vanilla buildings)
            m_BuildingsWithContracts.Clear();

            int shadyCount = 0;
            int totalCount = 0;
            int currentDay = TryGetCurrentGameDay(out var statsGameDay) ? (int)statsGameDay : 0;
            var activeContracts = m_ActiveContractsJsonBuilder;
            activeContracts.Clear();
            activeContracts.Append('[');
            bool firstActiveContract = true;
            // FIX W1-M12: Defer ECB creation to first actual use (avoids empty buffer overhead)
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            int expiredCount = 0;

            // S12b-5 FIX: Collect expired shady building indices for counterfeit cleanup
            int expiredShadyBuildings = 0;
            using var expiredShadyBuildingKeys = new NativeHashSet<long>(4, Allocator.Temp);

            // PERF: chunk iteration — avoids SystemAPI.Query dependency tracker leak (+420% over session)
            NativeArray<ArchetypeChunk> contractChunks;
            using (PerformanceProfiler.MeasureDebug("MCS.Stats.ChunkSync"))
            {
                m_ContractDataTypeHandle.Update(this);
                m_EntityTypeHandle.Update(this);
                contractChunks = m_ContractIterQuery.ToArchetypeChunkArray(Unity.Collections.Allocator.Temp);
            }
            for (int ci = 0; ci < contractChunks.Length; ci++)
            {
                var chunk = contractChunks[ci];
                var contracts = chunk.GetNativeArray(ref m_ContractDataTypeHandle);
                var entities = chunk.GetNativeArray(m_EntityTypeHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var cd = contracts[i];
                    var entity = entities[i];
                    var buildingEntity = cd.GetBuildingEntity();

                    if (!m_BuildingLookup.HasComponent(buildingEntity) || m_DeletedLookup.HasComponent(buildingEntity))
                    {
                        if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                        ecb.DestroyEntity(entity);
                        expiredCount++;
                        continue;
                    }

                    // Cleanup expired contracts
                    // S12a-5 FIX: >= instead of > (off-by-one: contracts lasted DurationDays+1)
                    int expirationDay = cd.ContractStartDay + cd.ContractDurationDays;
                    if (currentDay >= expirationDay)
                    {
                        // S12b-5 FIX: Track shady contract buildings for CounterfeitBattery cleanup
                        if (cd.IsShady)
                        {
                            expiredShadyBuildingKeys.Add(BuildingIdentityKey.Pack(cd.Building.Index, cd.Building.Version));
                            expiredShadyBuildings++;
                        }

                        if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                        ecb.DestroyEntity(entity);
                        expiredCount++;
                        continue;
                    }

                    m_BuildingsWithContracts.TryAdd(cd.Building.Index, cd.Building.Version);
                    totalCount++;
                    if (cd.IsShady)
                        shadyCount++;

                    AppendActiveContractUiEntry(activeContracts, ref firstActiveContract, entity, cd, buildingEntity, currentDay);
                }
            }
            activeContracts.Append(']');

            // Single writer for CounterfeitBattery.Deleted — handles both:
            // 1. Expired shady contracts (S12b-5)
            // 2. District replacement cleanup (delegated from DMS via PendingCounterfeitCleanupDistricts)
            m_ModernizationService.CopyPendingCounterfeitCleanupDistricts(m_DmsCleanupDistricts);
            m_ModernizationService.CopyPendingCounterfeitCleanupBuildingKeys(m_DmsCleanupBuildingKeys);
            bool hasDmsCleanup = m_DmsCleanupDistricts.Count > 0;
            bool hasDmsKeyCleanup = m_DmsCleanupBuildingKeys.Count > 0;
            bool hasExpiredShady = expiredShadyBuildings > 0;

            if (hasExpiredShady || hasDmsCleanup || hasDmsKeyCleanup)
            {
                using (PerformanceProfiler.MeasureDebug("MCS.Stats.Counterfeit"))
                {
                    int counterfeitCleaned = 0;
                    m_CounterfeitBatteryTypeHandle.Update(this);
                    m_EntityTypeHandle.Update(this);
                    using var counterfeitChunks = m_CounterfeitBatteryQuery.ToArchetypeChunkArray(Unity.Collections.Allocator.Temp);
                    for (int ci = 0; ci < counterfeitChunks.Length; ci++)
                    {
                        var chunk = counterfeitChunks[ci];
                        var batteries = chunk.GetNativeArray(ref m_CounterfeitBatteryTypeHandle);
                        var cbEntities = chunk.GetNativeArray(m_EntityTypeHandle);

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var cb = batteries[i];
                            var cbEntity = cbEntities[i];
                            var buildingEntity = cb.Building.ToEntity();

                            bool fromExpiry = hasExpiredShady
                                && expiredShadyBuildingKeys.Contains(BuildingIdentityKey.Pack(cb.Building.Index, cb.Building.Version));
                            bool fromInstallIdentity = hasDmsKeyCleanup
                                && m_DmsCleanupBuildingKeys.Contains(BuildingIdentityKey.Pack(cb.Building.Index, cb.Building.Version));

                            // FIX W9-H1: Only cleanup counterfeits for buildings whose district is in pending cleanup.
                            // Demolished buildings (version mismatch): leave for ModEntityCleanupSystem.
                            // Previous W6-M1 fix used fromDistrict=true for demolished → deleted counterfeits
                            // from wrong districts when multiple districts had pending cleanup.
                            bool fromDistrict = false;
                            if (hasDmsCleanup)
                            {
                                if (m_CurrentDistrictLookup.TryGetComponent(buildingEntity, out var district))
                                    fromDistrict = m_DmsCleanupDistricts.Contains(district.m_District.Index);
                                // Demolished building: cannot verify district. Leave for ModEntityCleanupSystem.
                            }

                            if (fromExpiry || fromDistrict || fromInstallIdentity)
                            {
                                if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                                ecb.AddComponent<Deleted>(cbEntity);

                                if (m_LinkReader.TryGet(BuildingRef.FromEntity(buildingEntity), out var modEntity)
                                    && !m_DeletedLookup.HasComponent(modEntity))
                                {
                                    ecb.AddComponent<Deleted>(modEntity);
                                    // No building write: deleting the BackupPower entity drops the building
                                    // from the link map on the next owner rebuild (WithNone<Deleted>).
                                }

                                counterfeitCleaned++;
                            }
                        }
                    }
                    if (counterfeitCleaned > 0)
                        Log.Info($"Cleaned up {counterfeitCleaned} counterfeit batteries (expiry: {hasExpiredShady}, district: {hasDmsCleanup})");
                    if (hasDmsCleanup || hasDmsKeyCleanup) m_ModernizationService.ClearPendingCounterfeitCleanup();
                }
            }

            if (hasEcb)
            {
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
                if (expiredCount > 0)
                    Log.Info($"Cleaned up {expiredCount} expired contracts");
            }

            WriteContractStatsSingleton(shadyCount, totalCount);

#pragma warning disable CIVIC458 // Maintenance contract UI uses a plain owner-side read model, not VersionedView publication.
            m_UiModel = new MaintenanceContractUiSnapshot(
                shadyCount,
                totalCount,
                activeContracts.ToString(),
                m_UiModel.HasPendingOffer,
                m_UiModel.PendingOffer);
#pragma warning restore CIVIC458
        }

        internal MaintenanceContractUiSnapshot GetUiSnapshot()
            => m_UiModel;

        [CompletesDependency("SeedUiModelAfterLoad: one-shot post-load owner-side UI read-model seed for paused/cold load. Reuses the contract chunk scan and pending-offer query outside UI; does not perform gameplay cleanup.")]
        private void SeedUiModelAfterLoad(int currentDay)
        {
            m_PendingProcurementLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_ContractDataTypeHandle.Update(this);
            m_EntityTypeHandle.Update(this);

            if (m_BuildingsWithContracts.IsCreated)
                m_BuildingsWithContracts.Clear();

            int shadyCount = 0;
            int totalCount = 0;
            var activeContracts = m_ActiveContractsJsonBuilder;
            activeContracts.Clear();
            activeContracts.Append('[');
            bool firstActiveContract = true;

            using (var contractChunks = m_ContractIterQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int ci = 0; ci < contractChunks.Length; ci++)
                {
                    var chunk = contractChunks[ci];
                    var contracts = chunk.GetNativeArray(ref m_ContractDataTypeHandle);
                    var entities = chunk.GetNativeArray(m_EntityTypeHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var cd = contracts[i];
                        var entity = entities[i];
                        var buildingEntity = cd.GetBuildingEntity();

                        if (!m_BuildingLookup.HasComponent(buildingEntity) || m_DeletedLookup.HasComponent(buildingEntity))
                            continue;

                        int expirationDay = cd.ContractStartDay + cd.ContractDurationDays;
                        if (currentDay >= expirationDay)
                            continue;

                        if (m_BuildingsWithContracts.IsCreated)
                            m_BuildingsWithContracts.TryAdd(cd.Building.Index, cd.Building.Version);

                        totalCount++;
                        if (cd.IsShady)
                            shadyCount++;

                        AppendActiveContractUiEntry(activeContracts, ref firstActiveContract, entity, cd, buildingEntity, currentDay);
                    }
                }
            }

            activeContracts.Append(']');

#pragma warning disable CIVIC458 // Maintenance contract UI uses a plain owner-side read model, not VersionedView publication.
            m_UiModel = new MaintenanceContractUiSnapshot(
                shadyCount,
                totalCount,
                activeContracts.ToString(),
                hasPendingOffer: false,
                PendingProcurementOfferRaw.Empty);
#pragma warning restore CIVIC458

            SeedPendingOfferUiModelAfterLoad();
            WriteContractStatsSingleton(shadyCount, totalCount);
        }

        private void WriteContractStatsSingleton(int shadyCount, int totalCount)
        {
            if (SystemAPI.TryGetSingletonRW<ContractStatsSingleton>(out var statsRef))
            {
                statsRef.ValueRW.ShadyContractCount = shadyCount;
                statsRef.ValueRW.TotalContractCount = totalCount;
            }
        }

        private void SeedPendingOfferUiModelAfterLoad()
        {
            if (m_PendingOfferQuery.IsEmpty)
                return;

            using var entities = m_PendingOfferQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
#pragma warning disable CIVIC485 // Post-load UI seed needs presence + enabled check for the durable pending offer.
                if (!m_PendingProcurementLookup.HasComponent(entity)
                    || !m_PendingProcurementLookup.IsComponentEnabled(entity))
                    continue;
#pragma warning restore CIVIC485

                var pending = m_PendingProcurementLookup[entity];
                if (!IsRecoverablePendingProcurement(entity, pending))
                    continue;

                SetPendingOfferUiModel(entity, pending);
                return;
            }
        }

        internal void RecordPendingOfferCleared(Entity entity)
        {
            if (!m_UiModel.HasPendingOffer)
                return;

            if (m_UiModel.PendingOffer.EntityIndex != entity.Index
                || m_UiModel.PendingOffer.EntityVersion != entity.Version)
                return;

            ClearPendingOfferUiModel();
        }

        internal static void ConsumePendingProcurementOffer(ref ComponentLookup<PendingProcurement> pendingLookup, Entity entity, string reason)
        {
#pragma warning disable CIVIC485 // Terminal cleanup must see disabled consumed payloads as well as active offers.
            if (!pendingLookup.HasComponent(entity))
                return;
#pragma warning restore CIVIC485

            var pending = pendingLookup[entity];
            pending.Lifecycle = PendingProcurementLifecycle.Consumed;
            pendingLookup[entity] = pending;

            if (pendingLookup.IsComponentEnabled(entity))
                pendingLookup.SetComponentEnabled(entity, false);
        }

        private void AppendActiveContractUiEntry(StringBuilder sb, ref bool first, Entity contractEntity, in ContractData contract, Entity buildingEntity, int currentDay)
        {
            if (contract.ContractDurationDays <= 0)
                return;

            try
            {
                int daysRemaining = System.Math.Max(0, (contract.ContractStartDay + contract.ContractDurationDays) - currentDay);
                var entry = new ActiveContractEntry
                {
                    EntityIndex = contractEntity.Index,
                    BuildingName = GetBuildingName(buildingEntity),
                    ContractType = contract.Type.ToString(),
                    VendorName = ContractVendors.GetVendorNameByHash(contract.VendorNameHash, contract.Type, contract.IsShady),
                    Quality = contract.Quality,
                    KickbackAmount = contract.KickbackAmount,
                    IsShady = contract.IsShady,
                    DaysRemaining = daysRemaining,
                };

                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"Skipping contract {contractEntity.Index}: {ex}");
            }
        }

        // ============================================================================
        // OFFER GENERATION
        // ============================================================================

        private void TryGenerateOffer(float currentGameDay)
        {
            if (m_ToastService == null)
            {
                Log.Debug(" ToastService not available");
                return;
            }

            if (m_PowerPlantQuery.IsEmpty) return;

            uint dayBucket = (uint)math.max(0, (int)System.Math.Round(currentGameDay * OFFER_SEED_DAY_BUCKET_SCALE));
            uint seed = unchecked((m_SimulationSystem.frameIndex + 1u) * OFFER_SEED_FRAME_MULTIPLIER
                ^ (dayBucket + 1u) * OFFER_SEED_DAY_MULTIPLIER);
            if (seed == 0u) seed = 1u;
            var random = new Random(seed);

            // Calculate offer chance scaled by elapsed game time
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null)
            {
                Log.Error("[MaintenanceContractSystem] GameTimeSystem unavailable — skipping contract offers");
                return;
            }
            float currentGameHours = timeProvider.Current.TotalGameHours;
            if (m_PrevGameHours < PREV_GAME_HOURS_INIT_EPSILON)
            {
                m_PrevGameHours = currentGameHours;
                return; // Skip first update after load — prevents huge delta spike
            }
            float deltaGameHours = (float)System.Math.Max(0.0, currentGameHours - m_PrevGameHours);
            m_PrevGameHours = currentGameHours;

            float hourlyChance = Core.Config.BalanceConfig.Current.CorruptionEvents.ProcurementOfferChancePerDay / GameRate.HOURS_PER_DAY;
            float frequencyMult = m_ReputationService.GetFrequencyMultiplier();
            // FIX W2-M1: Saturate to [0,1] to prevent >1.0 overshoot at large deltaGameHours
            float adjustedChance = math.saturate(hourlyChance * frequencyMult * deltaGameHours);

            // PERF: chunk iteration — avoids SystemAPI.Query dependency tracker leak (41ms spikes)
            m_ElectricityProducerTypeHandle.Update(this);
            m_EntityTypeHandle.Update(this);
            var plantChunks = m_PowerPlantQuery.ToArchetypeChunkArray(Unity.Collections.Allocator.Temp);
            bool offerGenerated = false;
            for (int ci = 0; ci < plantChunks.Length && !offerGenerated; ci++)
            {
                var chunk = plantChunks[ci];
                var entities = chunk.GetNativeArray(m_EntityTypeHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = entities[i];

                    // Skip if building already has a contract (ContractData on separate entity)
                    if (m_BuildingsWithContracts.TryGetValue(entity.Index, out var contractVersion)
                        && contractVersion == entity.Version)
                        continue;

                    if (random.NextFloat() > adjustedChance)
                        continue;

                    // Generate offer for this power plant
                    GenerateOfferForEntity(entity, random.NextUInt());
                    m_LastOfferGameDay = currentGameDay;
                    offerGenerated = true;
                    break; // Only one offer at a time
                }
            }

        }

        private void GenerateOfferForEntity(Entity entity, uint seed)
        {
            var random = new Random(seed);
            var procurement = BalanceConfig.Current.Procurement;

            // Randomly choose contract type based on config ratio
            ContractType contractType = random.NextFloat() < procurement.MaintenanceVsSupplyRatio
                ? ContractType.Maintenance
                : ContractType.Supply;

            // Get vendor templates for this contract type
            var officialVendor = ContractVendors.GetRandomOfficial(seed, contractType);
            var shadyVendor = ContractVendors.GetRandomShady(seed ^ 0xDEADBEEFu, contractType);

            // Calculate prices
            int basePrice = (int)procurement.BaseMaintenanceCost;
            int officialPrice = (int)System.Math.Round(basePrice * officialVendor.PriceMultiplier);
            int shadyPrice = (int)System.Math.Round(basePrice * shadyVendor.PriceMultiplier);
            int savings = officialPrice - shadyPrice;
            int kickback = (int)System.Math.Round(savings * shadyVendor.KickbackPercent);

            // Create pending offer as ECS component on building entity
            // NOTE: Store Entity as Index+Version to avoid vanilla orphan detection
            var pendingOffer = new PendingProcurement
            {
                Service = CityService.Electricity,
                Type = contractType,
                TargetBuilding = BuildingRef.FromEntity(entity),
                OfficialPrice = officialPrice,
                ShadyPrice = shadyPrice,
                KickbackOffer = kickback,
                OfficialQuality = officialVendor.Quality,
                ShadyQuality = shadyVendor.Quality,
                OfficialVendorHash = officialVendor.GetNameHash(),
                ShadyVendorHash = shadyVendor.GetNameHash(),
                Lifecycle = PendingProcurementLifecycle.Active
            };

            // FIX W2-H2: Toast-first pattern — queue toast BEFORE creating component.
            // QueueToast can fail (cooldown, queue full). If we create the component first
            // and toast fails, SetComponentEnabled on ECB-pending component crashes.
            if (!TryQueueProcurementToast(entity, contractType, officialPrice, shadyPrice, kickback))
                return;

            // Add or update component on building entity (only after toast confirmed)
#pragma warning disable CIVIC485 // Offer creation tests archetype presence to choose AddComponent vs enable existing disabled state.
            if (m_PendingProcurementLookup.HasComponent(entity))
#pragma warning restore CIVIC485
            {
                m_PendingProcurementLookup[entity] = pendingOffer;
                m_PendingProcurementLookup.SetComponentEnabled(entity, true);
            }
            else
            {
                var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                ecb.AddComponent(entity, pendingOffer);
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
            }

            SetPendingOfferUiModel(entity, pendingOffer);

            if (Log.IsDebugEnabled) Log.Debug($"Generated {contractType} offer for entity {entity.Index}");
        }

        /// <summary>
        /// Queue the procurement-offer toast and record its tracking ids. Shared by
        /// fresh-offer generation and post-load reconciliation so a save/loaded
        /// offer (durable PendingProcurement) gets its transient toast rebuilt
        /// instead of surviving invisibly. Returns false if ToastService is
        /// unavailable or the toast was rejected (cooldown / queue full).
        /// </summary>
        private bool TryQueueProcurementToast(Entity entity, ContractType contractType, int officialPrice, int shadyPrice, int kickback)
        {
            string buildingName = GetBuildingName(entity);
            string typeLabel = contractType == ContractType.Supply ? "Fuel Supply" : "Maintenance";
            string qualityWarning = contractType == ContractType.Supply
                ? "Lower quality = reduced efficiency"
                : "Lower quality = more breakdowns";

            if (m_ToastService == null) return false;
#pragma warning disable CA1308 // Normalize strings to uppercase - display text uses lowercase
            int queuedToastId = m_ToastService.QueueToastId(
                ToastType.ProcurementOffer,
                $"{typeLabel} Contract",
                $"{buildingName} needs {typeLabel.ToLowerInvariant()}. Official: ${officialPrice:N0} or \"friend's\" company: ${shadyPrice:N0} (+${kickback:N0} to you). {qualityWarning}.",
                "View Options",
                "Not Now",
                ToastPriority.Normal,
                entity.Index,
                bypassOfferCooldown: true
            );
#pragma warning restore CA1308

            if (queuedToastId <= 0)
            {
                Log.Debug(" Toast rejected (cooldown or queue full), skipping offer");
                return false;
            }

            m_LastProcurementToastId = queuedToastId;
            m_ProcurementToastBuildingIndex = entity.Index;
            m_ProcurementToastBuildingVersion = entity.Version;
            return true;
        }

        // ============================================================================
        // TOAST EVENT SUBSCRIPTION
        // ============================================================================

        /// <summary>
        /// FIX W6-H1: Subscribe to toast events if service is available and not yet subscribed.
        /// Called from both OnStartRunning and OnThrottledUpdate to handle late ToastService registration
        /// (UIUpdate phase registers after GameSimulation phase may already be running).
        /// </summary>
        private void SubscribeToastEvents()
        {
            if (m_ToastEventsSubscribed || m_ToastService == null) return;

            // FIX W2-H1: Unsubscribe first to prevent duplicate handlers on system restart
            m_ToastService.OnToastExpired -= OnToastExpired;
            m_ToastService.OnToastExpired += OnToastExpired;
            m_ToastService.OnToastInteracted -= OnToastInteracted;
            m_ToastService.OnToastInteracted += OnToastInteracted;
            m_ToastEventsSubscribed = true;
        }

        private ToastService? ResolveToastService()
        {
            var currentToast = ServiceRegistry.TryGet<ToastService>();
            if (currentToast == m_ToastService)
            {
                SubscribeToastEvents();
                return m_ToastService;
            }

            if (m_ToastService != null)
            {
                m_ToastService.OnToastExpired -= OnToastExpired;
                m_ToastService.OnToastInteracted -= OnToastInteracted;
                m_ToastEventsSubscribed = false;
            }

            m_ToastService = currentToast;
            SubscribeToastEvents();
            return m_ToastService;
        }

        private void ResolveFeatureServices()
        {
            m_ReputationService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowReputationService.Instance);
            m_ModernizationService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDistrictModernizationService.Instance);
        }

        // ============================================================================
        // TOAST EVENT HANDLERS
        // ============================================================================

        /// <summary>
        /// Called when toast expires (real-time timeout or user dismisses).
        /// Only clears pending offer for procurement toasts.
        /// </summary>
        private void OnToastExpired(int toastId)
        {
            // FIX W2-M6: Defer to flag — this fires from UIUpdate phase (ToastUISystem)
            if (toastId == m_LastProcurementToastId)
            {
                m_PendingClearToastId = toastId;
                m_PendingClearReason = "expired";
            }
        }

        /// <summary>
        /// Called when user interacts with toast (accept/reject).
        /// Only clears pending offer for procurement toasts (not notification toasts).
        /// Accept is handled by ContractResponseSystem.
        /// </summary>
        private void OnToastInteracted(int toastId, bool wasAccepted)
        {
            // FIX W2-M6: Defer to flag — this fires from UIUpdate phase (ToastUISystem)
            // Accept means "View Options"; the pending offer must remain until the
            // procurement panel emits the actual ContractResponse.
            if (toastId == m_LastProcurementToastId && !wasAccepted)
            {
                m_PendingClearToastId = toastId;
                m_PendingClearReason = "rejected";
            }
        }

        /// <summary>
        /// Clears any active pending offer by marking it consumed and disabling the component.
        /// </summary>
        private void ClearPendingOfferByToast(int toastId, string reason)
        {
            // FIX CIVIC218: Use SystemAPI.Query instead of ToEntityArray (avoids sync point)
            // Query already filters disabled IEnableableComponent — no IsComponentEnabled check needed
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<PendingProcurement>>()
                .WithAll<Building>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                if (entity.Index != m_ProcurementToastBuildingIndex
                    || entity.Version != m_ProcurementToastBuildingVersion)
                    continue;

                ConsumePendingProcurementOffer(ref m_PendingProcurementLookup, entity, reason);
                if (Log.IsDebugEnabled) Log.Debug($"Toast #{toastId} {reason}, cleared pending offer on entity {entity.Index}");
                m_LastProcurementToastId = -1;
                m_ProcurementToastBuildingIndex = -1;
                m_ProcurementToastBuildingVersion = -1;
                ClearPendingOfferUiModel();
                return;
            }
        }

        // ============================================================================
        // NOTIFICATION TOAST EVENT HANDLERS
        // ============================================================================

        private void OnInvestigationStarted(InvestigationStartedEvent evt)
        {
            ForceNextUpdate();
            var toast = ResolveToastService();
            if (toast == null) { Log.Warn("ToastService unavailable for InvestigationStarted"); return; }
            int toastId = toast.QueueToastId(
                ToastType.AuditorWarning,
                "Auditor Warning",
                $"Auditor flagged suspicious activity. {evt.JournalistName} is asking questions.",
                "Noted", "Dismiss",
                ToastPriority.High);
            if (toastId <= 0) Log.Warn("Auditor warning toast was rejected");
        }

        private void OnCorruptionNarrative(CorruptionNarrativeEvent evt)
        {
            if (evt.Type != CorruptionNarrativeEventType.PoliceInvestigation) return;

            var toast = ResolveToastService();
            if (toast == null) { Log.Warn("ToastService unavailable for CorruptionNarrative"); return; }
            int toastId = toast.QueueToastId(
                ToastType.InsuranceClaim,
                "Insurance Fraud Detected",
                "Police found evidence of insurance fraud in maintenance contracts.",
                "Noted", "Dismiss",
                ToastPriority.Critical);
            if (toastId <= 0) Log.Warn("Insurance fraud toast was rejected");
        }

        private void OnInfraEvent(InfraEvent evt)
        {
            if (evt.Type != InfraEventType.EquipmentExplosion) return;

            var toast = ResolveToastService();
            if (toast == null) { Log.Warn("ToastService unavailable for InfraEvent"); return; }
            int toastId = toast.QueueToastId(
                ToastType.SafetyAccident,
                "Safety Accident",
                "Equipment failure reported. Maintenance records are under review.",
                "Noted", "Dismiss",
                ToastPriority.Normal);
            if (toastId <= 0) Log.Warn("Safety accident toast was rejected");
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        private static bool TryGetCurrentGameDay(out float currentGameDay)
        {
            // FIX W9-M5: Use fractional day (TotalGameHours / 24) instead of integer CurrentDay.
            // Config values MinDaysBetweenOffers=0.5f and MinDaysBetweenAnyPopup=0.25f
            // require sub-day granularity. Original formula was frameIndex/86400 (fractional).
            // LOAD-INVARIANT: offer/stat refresh can run before GameTime activation after load.
            if (!GameTimeSystem.TryGetGameHours(out var gameHours))
            {
                currentGameDay = 0f;
                return false;
            }

            currentGameDay = gameHours / GameRate.HOURS_PER_DAY;
            return true;
        }

        private string GetBuildingName(Entity entity)
        {
            if (m_PrefabRefLookup.TryGetComponent(entity, out var prefabRef))
            {
                if (m_PrefabSystem.TryGetPrefab(prefabRef.m_Prefab, out PrefabBase prefab) && prefab.name != null)
                {
                    return prefab.name;
                }
            }
            return $"Power Plant #{entity.Index}";
        }

        private void SetPendingOfferUiModel(Entity entity, in PendingProcurement pending)
        {
#pragma warning disable CIVIC458 // Maintenance contract UI uses a plain owner-side read model, not VersionedView publication.
            m_UiModel = new MaintenanceContractUiSnapshot(
                m_UiModel.ShadyContractCount,
                m_UiModel.TotalContractCount,
                m_UiModel.ActiveContractsJson,
                hasPendingOffer: true,
                new PendingProcurementOfferRaw(
                    entity.Index,
                    entity.Version,
                    pending.Service,
                    pending.Type,
                    pending.OfficialVendorHash,
                    pending.ShadyVendorHash,
                    pending.OfficialPrice,
                    pending.ShadyPrice,
                    pending.KickbackOffer,
                    pending.OfficialQuality,
                    pending.ShadyQuality,
                    GetBuildingName(entity)));
#pragma warning restore CIVIC458
        }

        private void ClearPendingOfferUiModel()
        {
#pragma warning disable CIVIC458 // Maintenance contract UI uses a plain owner-side read model, not VersionedView publication.
            m_UiModel = new MaintenanceContractUiSnapshot(
                m_UiModel.ShadyContractCount,
                m_UiModel.TotalContractCount,
                m_UiModel.ActiveContractsJson,
                hasPendingOffer: false,
                PendingProcurementOfferRaw.Empty);
#pragma warning restore CIVIC458
        }

        protected override void OnDestroy()
        {
            if (m_ToastService != null)
            {
                m_ToastService.OnToastExpired -= OnToastExpired;
                m_ToastService.OnToastInteracted -= OnToastInteracted;
                m_ToastEventsSubscribed = false;
            }

            UnsubscribeSafe<InvestigationStartedEvent>(OnInvestigationStarted);
            UnsubscribeSafe<CorruptionNarrativeEvent>(OnCorruptionNarrative);
            UnsubscribeSafe<InfraEvent>(OnInfraEvent);

            if (m_BuildingsWithContracts.IsCreated)
                m_BuildingsWithContracts.Dispose();

            base.OnDestroy();
        }
    }
}
