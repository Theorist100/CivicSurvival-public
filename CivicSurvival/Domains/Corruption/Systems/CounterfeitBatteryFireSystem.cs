using Game;
using Game.Areas;
using Game.Common;
using Game.Events;
using Unity.Entities;
using Unity.Collections;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Prefabs;
using Game.Simulation;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Periodically triggers fires in buildings with counterfeit batteries.
    ///
    /// Mechanics:
    /// - Every 60-90 days (per district with counterfeit equipment)
    /// - Picks random counterfeit building → event-backed mod fire
    /// - Vanilla FireSimulationSystem owns the building damage, repair cost and spread
    ///   (the mod issues no DamageChargeRequest — see ModFireApplySystem)
    /// - Guaranteed consequence of corrupt District Modernization
    ///
    /// Integration:
    /// - IDistrictModernizationService: reads district program data (via ServiceRegistry)
    /// - ICounterfeitFireDedupReader: published so BackupPowerEffectsSystem can skip
    ///   double-firing buildings already ignited here today.
    /// - Event Bus: publishes CounterfeitFireEvent
    ///
    /// AGENT NOTE (CRIT-02 FALSE POSITIVE):
    /// Entity passed to CounterfeitFireEvent is NOT unstable.
    /// CS2 mod is SINGLE-THREADED - EventBus.Publish() calls handlers SYNCHRONOUSLY.
    /// Entity cannot be demolished between Publish() and handler. Do NOT "fix" this.
    /// </summary>
    public partial class CounterfeitBatteryFireSystem : CivicSystemBase, IDefaultSerializable, IResettable, ICounterfeitFireDedupReader, IActGatedSystem
    {
        private const int RANDOM_SEED_OFFSET = 99999;
        private const uint DISTRICT_SEED_PRIME = 7919u;

        private static readonly LogContext Log = new("CounterfeitFire");

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        private EntityQuery m_CounterfeitQuery;
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private ComponentLookup<OnFire> m_OnFireLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private PrefabSystem m_PrefabSystem = null!;
        private NameSystemFacade? m_NameService;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private EntityQuery m_CurrentActQuery;
#pragma warning disable CIVIC324 // Ephemeral act-gate controller; recreated by OnCreate, reset paths, and Deserialize.
        [System.NonSerialized] private ActGateController m_Gate = null!;
#pragma warning restore CIVIC324
        // Shared cross-system Ignite/Destroy dedup. Per-system m_IgniteQueuedThisFrame
        // retired in favour of IFrameMutationDedup published by Mod.OnLoad.
        private IFrameMutationDedup m_FrameMutationDedup = null!;
        private DayChangedDedup m_DayDedup = default;
        private ModSettings? m_Settings;
        [System.NonSerialized] private readonly System.Collections.Generic.List<int> m_ActiveModernizationDistricts = new();
        [VersionCursorAllowed("Counterfeit fire consumes DistrictModernization ProgramsView directly to refresh its local active-district snapshot.")]
        [System.NonSerialized] private int m_ModernizationProgramsObserverCursor = -1;

        // Per-day set of buildings that received OnFire from this system.
        // Shared with BackupPowerEffectsSystem to prevent duplicate fire events on the same building.
        // Stores Entity (Index+Version) to avoid CIVIC097 index-recycling false positives.
        private readonly System.Collections.Generic.HashSet<Entity> m_FiredTodayEntities = new();

        /// <summary>Returns true if the building was already tagged for fire by this system today.</summary>
        public bool WasFiredToday(Entity buildingEntity) => m_FiredTodayEntities.Contains(buildingEntity);

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info($"{nameof(CounterfeitBatteryFireSystem)} created");

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // Query for CounterfeitBattery mod entities (vanilla isolation)
            m_CounterfeitQuery = GetEntityQuery(
                ComponentType.ReadOnly<CounterfeitBattery>(),
                ComponentType.Exclude<Deleted>()
            );

            // Lookups for vanilla building components
            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_OnFireLookup = GetComponentLookup<OnFire>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(true);
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            InitializeGate();

            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.StateChange);

            // Publish dedup reader so BackupPowerEffectsSystem can read across the
            // Corruption/PowerBackup boundary via Core interface (not concrete type).
            ServiceRegistry.Instance.Register<ICounterfeitFireDedupReader>(this);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            m_NameService ??= ServiceRegistry.Instance.Require<NameSystemFacade>();
            m_FrameMutationDedup ??= ServiceRegistry.Instance.Require<IFrameMutationDedup>();
        }

        protected override void OnUpdateImpl()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);
            // Fire checks remain event-driven in OnDayChanged.
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);
            // Instance-matching unregister avoids holding a stale reference into the
            // next world if ServiceRegistry already accepted a replacement.
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<ICounterfeitFireDedupReader>(this);
            // FrameMutationDedup is process-lifetime singleton owned by Mod — do not dispose here.
            Log.Info($"{nameof(CounterfeitBatteryFireSystem)} destroyed");
            base.OnDestroy();
        }

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (isInitial)
                    return;

                ResetOpenGateState();
                Log.Info("[CounterfeitFire] Gate opened");
                return;
            }

            if (next == ActGateState.Inactive && !isInitial)
            {
                m_DayDedup.Reset();
                m_FiredTodayEntities.Clear();
                Log.Info("[CounterfeitFire] Gate closed");
            }
        }

        private void ResetOpenGateState()
        {
            m_DayDedup.Reset();
            m_FiredTodayEntities.Clear();
            m_ActiveModernizationDistricts.Clear();
            m_ModernizationProgramsObserverCursor = -1;
        }

        private void OnDayChanged(DayChangedEvent evt)
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);
            if (m_Gate.State != ActGateState.Active) return;
            if (m_Settings != null && !m_Settings.BackupPowerEnabled) return;
            if (m_DayDedup.AlreadyProcessed(evt.DayNumber)) return;

            // Reset per-day fire tracking for the new day
            m_FiredTodayEntities.Clear();

            using var _ = PerformanceProfiler.Measure("CounterfeitBatteryFire.OnDayChanged");
            m_CurrentDistrictLookup.Update(this);
            m_OnFireLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            // Same-domain access (Corruption). Null object yields no active programs if producer is unavailable.
            var procurementService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDistrictModernizationService.Instance);

            // Hoist outside loop: one sync point + one allocation instead of N per district
            var counterfeitDatas = m_CounterfeitQuery.ToComponentDataArray<CounterfeitBattery>(Allocator.Temp);
            try
            {
                foreach (var districtIndex in GetActiveProgramDistricts(procurementService))
                {
                    var maybeProgram = procurementService.GetProgram(districtIndex);
                    if (maybeProgram is not { } program)
                        continue;

                    // Only corrupt districts
                    if (program.Contractor != Core.Types.ContractorType.YourGuy)
                        continue;

                    // Per-district deterministic seed: independent of Dictionary iteration order
                    uint seed = unchecked((uint)evt.DayNumber + (uint)RANDOM_SEED_OFFSET + (uint)districtIndex * DISTRICT_SEED_PRIME);
                    if (seed == 0) seed = 0x42504553u;
                    var random = new Unity.Mathematics.Random(seed);

                    // Check if enough time passed since last fire
                    // Fire interval: random 60-90 days per district
                    var spCfg = BalanceConfig.Current.ShadowProcurement;
                    int daysSinceLastFire = evt.DayNumber - program.LastFireDay;
                    int fireInterval = spCfg.FireMinDays >= spCfg.FireMaxDays
                        ? spCfg.FireMinDays
                        : random.NextInt(spCfg.FireMinDays, spCfg.FireMaxDays);

                    if (daysSinceLastFire < fireInterval)
                        continue;

                    // TRIGGER FIRE
                    if (!TriggerFireInDistrict(districtIndex, ref random, counterfeitDatas))
                        continue;

                    // Update fire tracking via Data-Driven Command (Axiom 7: ECB for structural changes)
                    var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    var requestEntity = ecb.CreateEntity();
                    ecb.AddComponent(requestEntity, new FireRecordRequest
                    {
                        DistrictIndex = districtIndex,
                        DayNumber = evt.DayNumber
                    });
                    RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(FireRecordRequest), districtIndex.ToString());
                    // ECB written on main thread — no AddJobHandleForProducer needed
                }
            }
            finally
            {
                if (counterfeitDatas.IsCreated) counterfeitDatas.Dispose();
            }
        }

        private System.Collections.Generic.IReadOnlyList<int> GetActiveProgramDistricts(IDistrictModernizationService procurementService)
        {
            IVersionedView<ModernizationProgramsSnapshot>? programsView = procurementService.ProgramsView;
            if (programsView == null)
                return System.Array.Empty<int>();

            if (programsView.Observe(ref m_ModernizationProgramsObserverCursor).Changed)
            {
                m_ActiveModernizationDistricts.Clear();
                foreach (int districtIndex in procurementService.ActiveProgramDistricts)
                    m_ActiveModernizationDistricts.Add(districtIndex);
            }

            return m_ActiveModernizationDistricts;
        }

        private bool TriggerFireInDistrict(int districtIndex, ref Unity.Mathematics.Random random, NativeArray<CounterfeitBattery> counterfeitDatas)
        {
            // Find all counterfeit buildings in this district
            var candidates = new NativeList<Entity>(Allocator.Temp);
            Entity targetBuilding = Entity.Null;
            try
            {
                for (int i = 0; i < counterfeitDatas.Length; i++)
                {
                    var buildingEntity = counterfeitDatas[i].GetBuildingEntity();
                    if (!EntityManager.Exists(buildingEntity))
                        continue;
                    if (m_DeletedLookup.HasComponent(buildingEntity))
                        continue;
                    if (m_DestroyedLookup.HasComponent(buildingEntity))
                        continue;
                    if (m_OnFireLookup.HasComponent(buildingEntity) || m_FiredTodayEntities.Contains(buildingEntity))
                        continue;
                    // Get district from vanilla building
                    if (!m_CurrentDistrictLookup.TryGetComponent(buildingEntity, out var district))
                        continue;

                    if (district.m_District != Entity.Null && district.m_District.Index == districtIndex)
                        candidates.Add(buildingEntity);
                }

                if (candidates.Length == 0)
                    return false;

                // Pick random building
                targetBuilding = candidates[random.NextInt(candidates.Length)];
            }
            finally
            {
                if (candidates.IsCreated) candidates.Dispose();
            }

            if (targetBuilding == Entity.Null || m_FiredTodayEntities.Contains(targetBuilding))
                return false;

            var fireEcb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            // TryApplyModFire is the producer half: it creates an event-backed ModFireIntent on
            // the GameSimulation buffer. ModFireApplySystem builds the real OnFire.m_Event from
            // the vanilla fire prefab and applies OnFire + BatchesUpdated + upgrade propagation
            // in ModificationEnd, in phase with the render pass. Vanilla FireSimulationSystem
            // then owns the building damage and repair cost — the mod issues no DamageChargeRequest
            // so the fire is not double-charged.
            if (!BuildingDamageHelper.TryApplyModFire(
                fireEcb,
                targetBuilding,
                m_FrameMutationDedup,
                m_OnFireLookup,
                m_DestroyedLookup,
                m_DeletedLookup))
                return false;

            m_FiredTodayEntities.Add(targetBuilding);

            // Publish fire event — resolve building name now (Entity may be reused later)
            string buildingName = ResolveBuildingName(targetBuilding);
            EventBus?.SafePublish(new ShadowNarrativeEvent(
                ShadowNarrativeEventType.CounterfeitFire,
                BuildingIndex: targetBuilding.Index,
                DistrictIndex: districtIndex,
                BuildingName: buildingName
            ), "CounterfeitBatteryFireSystem");

            Log.Warn($"[CounterfeitFire] District {districtIndex}: Fire in {buildingName} ({targetBuilding.Index})");
            return true;
        }

        private string ResolveBuildingName(Entity building)
        {
            string renderedName = m_NameService?.GetRenderedLabelName(building)!;
            if (!string.IsNullOrWhiteSpace(renderedName))
                return renderedName;

            if (m_PrefabRefLookup.TryGetComponent(building, out var prefabRef) &&
                m_PrefabSystem.TryGetPrefab(prefabRef.m_Prefab, out PrefabBase prefab) &&
                prefab.name != null)
            {
                return prefab.name;
            }
            return $"Building #{building.Index}";
        }
    }
}
