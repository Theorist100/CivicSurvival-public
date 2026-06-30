using System;
using System.Text;
using CivicSurvival.Core.UI;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using Game.Prefabs;
using Game.UI;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Domain.Engineering;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Interfaces.Domain.ThreatDamage;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Services;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.PowerGrid.UI
{
    /// <summary>
    /// UI system for core power grid data.
    /// Uses GetEntityQuery (auto-disposed), cached services.
    ///
    /// Migrated from PowerGridUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.CitySchedule)]
    public partial class PowerGridUISystem : CivicUIPanelSystem
    {
        private const float NOMINAL_FREQUENCY_HZ = 50.0f;
        private const float SHADOW_MARKUP_EPSILON = 0.0001f;
        private const int PLANTS_JSON_INITIAL_CAPACITY = 2048;
        private const int CIVILIAN_DAMAGE_JSON_INITIAL_CAPACITY = 2048;

        private EndFrameBarrier m_EndFrameBarrier = null!;

        // ECS queries
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_StressDataQuery;
        private EntityQuery m_AutoDispatchQuery;
        private EntityQuery m_WalletQuery;
        private EntityQuery m_ThresholdQuery;
        private EntityQuery m_DistrictBufferQuery;
        private EntityQuery m_WaveStateQuery;

        // Cached services
        // CA2213: We don't own m_DistrictState - ServiceRegistry owns and disposes it
#pragma warning disable CA2213
        private IDistrictStateReader m_DistrictState = null!;
        private IDistrictStateWriter m_DistrictStateWriter = null!;
#pragma warning restore CA2213
        private IEquipmentUIService m_WearUIService = null!;
        private IPowerCapacitySnapshotReader? m_PowerCapacitySnapshotReader;
        private IPlantRepairIntentReader m_PlantRepairIntentReader = null!;
        private ICivilianDamageReader m_CivilianDamageReader = null!;
        private IImportCapVersionReader m_ImportCapVersionReader = null!;
        private IServedLoadVersionReader m_ServedLoadVersionReader = null!;
        private IBlackoutStateVersionReader m_BlackoutStateVersionReader = null!;
        private IAutoDispatchVersionReader m_AutoDispatchVersionReader = null!;
        private ICollapseOwnerVersionReader m_CollapseOwnerVersionReader = null!;
        private NameSystem m_NameSystem = null!;
        private EntityStorageInfoLookup m_EntityStorageInfoLookup;
        private GameTimeSystem? m_TimeProvider;
        [NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        private bool m_StressDataWarned;

        // PERF: Dirty-check for serialized JSON — skip rebuild when source data version unchanged
        private string m_LastGenerationSourcesJson = "";
        private string m_LastCivilianDamageJson = "";
        // Cache-input views: private struct with value-only fields. default(T) is valid here
        // (all int/long/float/bool zeroes), so no Empty constant is needed — the type is
        // an internal cache key, not a published snapshot.
        private readonly VersionedView<GenerationSourcesCacheInput> m_GenerationSourcesCacheInputView = new(default);
        private readonly VersionedView<CivilianDamageCacheInput> m_CivilianDamageCacheInputView = new(default);
        private int m_GenerationSourcesInputCursor;
        private int m_RepairIntentObserverCursor = int.MinValue;
        private int m_CivilianDamageInputCursor;
        private int m_CivilianDamageSnapshotCursor;
        private int m_ImportCapSnapshotCursor;
        private int m_ServedLoadSnapshotCursor;
        private int m_BlackoutStateSnapshotCursor;
        private int m_AutoDispatchOwnershipSnapshotCursor;
        private int m_CollapseOwnerSnapshotCursor;
        [NonSerialized] private CivilianDamageSnapshot m_CurrentCivilianDamageSnapshot = CivilianDamageSnapshot.Empty;
        private int m_LastAtRiskPlantCount;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_PowerGridQuery = GetEntityQuery(
                ComponentType.ReadOnly<PowerGridSingleton>());

            // Show-defaults convention: panel always renders with a neutral DTO when
            // producers are not yet available — no RequireForUpdate, no fail-loud
            // GetSingletonOrDefault (which threw [CRITICAL] producer-not-yet-run).
            m_StressDataQuery = GetEntityQuery(
                ComponentType.ReadOnly<GridStressData>());
            m_AutoDispatchQuery = GetEntityQuery(
                ComponentType.ReadOnly<AutoDispatchData>());
            m_WalletQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowWalletSingleton>());
            m_ThresholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<ThresholdStateSingleton>());
            m_DistrictBufferQuery = GetEntityQuery(
                ComponentType.ReadOnly<DistrictPowerBufferSingleton>());
            m_WaveStateQuery = GetEntityQuery(
                ComponentType.ReadOnly<WaveStateSingleton>());

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_EntityStorageInfoLookup = GetEntityStorageInfoLookup();
            m_DependencyWire = new CivicDependencyWire(nameof(PowerGridUISystem));

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Self-wiring: resolve services when system actually runs (all domains created)
            m_DistrictState = m_DependencyWire.RequireWired(() => ServiceRegistry.Instance.Require<IDistrictStateReader>());
            m_DistrictStateWriter = m_DependencyWire.RequireWired(() => ServiceRegistry.Instance.Require<IDistrictStateWriter>());
            m_PlantRepairIntentReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullPlantRepairIntentReader.Instance));
            m_CivilianDamageReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCivilianDamageReader.Instance));
            m_WearUIService = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullEquipmentUIService.Instance));
            m_ImportCapVersionReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullImportCapVersionReader.Instance));
            m_ServedLoadVersionReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullServedLoadVersionReader.Instance));
            m_BlackoutStateVersionReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullBlackoutStateVersionReader.Instance));
            m_AutoDispatchVersionReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullAutoDispatchVersionReader.Instance));
            m_CollapseOwnerVersionReader = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCollapseOwnerVersionReader.Instance));
            // IPowerCapacitySnapshotReader — AlwaysOpen-mandatory (Engineering), резолвится ??= Require
            // в update (CIVIC463: не TryGet; CIVIC302: re-resolve в update, не залипает null).
            m_TimeProvider ??= GameTimeSystem.Instance;

            // Force rebuild of cached JSON on load (service snapshots may have changed)
            m_GenerationSourcesInputCursor = 0;
            m_RepairIntentObserverCursor = int.MinValue;
            m_CivilianDamageInputCursor = 0;
            m_ImportCapSnapshotCursor = 0;
            m_ServedLoadSnapshotCursor = 0;
            m_BlackoutStateSnapshotCursor = 0;
            m_AutoDispatchOwnershipSnapshotCursor = 0;
            m_CollapseOwnerSnapshotCursor = 0;
            m_LastGenerationSourcesJson = "";
            m_LastCivilianDamageJson = "";
        }

        protected override void ConfigureBindings()
        {
            // Domain JSON binding (15 fields)
            Bindings.Add<string>(PowerGridState, "{}");

            // IsCollapsed no longer needed as separate binding
        }

        protected override void ConfigureTriggers()
        {
            Triggers.AddPhaseSafeTrigger<int, int>(RepairPlant, FeatureIds.PowerGrid, RequestResultBridge.PlantRepair, ActionKey.PlantRepair, BuildActionContext, OnRepairPlant);
            Triggers.AddPhaseSafeTrigger<EntityRef, int>(RepairCivilian, FeatureIds.PowerGrid, RequestResultBridge.CivilianRepair, ActionKey.CivilianRepair, BuildActionContext, OnRepairCivilian);
            // City schedule is not phase-gated: switching presets during a wave is
            // how the player saves electricity by cutting non-critical districts.
            // ActionGate.Resolve(ActionKey.CitySchedule) returns Allow() now, and the
            // trigger no longer wraps the handler in InvokePhaseSafe.
            Triggers.Add<int>(SetCitySchedule, FeatureIds.PowerGrid, RequestResultBridge.CitySchedule, ActionKey.CitySchedule, BuildActionContext, OnSetCitySchedule);
            Triggers.Add(ToggleAutoDispatch, FeatureIds.PowerGrid, RequestResultBridge.AutoDispatchToggle, OnToggleAutoDispatch);
        }

        protected override void OnPanelUpdate()
        {
            // Version readers resolved once in OnStartRunning — same-domain producers (PowerGrid)
            // register before any OnUpdate runs, so re-resolve is unnecessary.

            var dto = new PowerGridDto();

            // One read per update: BalanceConfig.Current can swap on hot-reload, and two
            // dereferences within the same pass could observe different config instances.
            var balanceCfg = BalanceConfig.Current;

            var grid = m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var pg)
                ? pg : PowerGridSingleton.Default;
            dto.GridStatus = grid.GetStatusString();
            dto.Production = grid.Production / 1000;
            dto.Demand = grid.Demand / 1000;
            dto.Consumption = grid.Consumption / 1000;

            // Fleet saturation + capacity aggregates — single source of truth: the resolver
            // snapshot (no second formula).
            m_PowerCapacitySnapshotReader ??= ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
            if (m_PowerCapacitySnapshotReader.TryGetSnapshot(out var capacitySnapshot))
            {
                dto.FleetSaturationFactor = capacitySnapshot.FleetTargetFactor;
                dto.CityDispatchableMW = capacitySnapshot.CityDispatchableMW;
                dto.CapacityHeadroomMW = capacitySnapshot.CityDispatchableMW - grid.Consumption / 1000;
                dto.HeadroomWarningMW = Math.Min(
                    capacitySnapshot.LargestPlantKW / 1000,
                    (int)balanceCfg.GenerationSaturation.UnitBufferCapMW);
            }
            else
            {
                // Snapshot not published yet (window ~0: the resolver is IPostLoadValidation,
                // HydrationOrder=25) — neutral values; the capacity DTO fields stay 0.
                dto.FleetSaturationFactor = 1f;
            }
            // ExternalPower (the donor/import bonus sits in Production) is not export, and
            // the flow-difference proxy carries ±a-few-MW rounding noise — clamped by the
            // enforced ceiling (cap × interconnectors) so the EXPORT row cannot show
            // phantom MW while the cap is 0 (one formula, PowerHeadroomMath).
            dto.GridExportMW = PowerHeadroomMath.ComputeLegalExportKW(
                grid.RawBalance, grid.ExternalPower, ImportCapRuntimeState.CurrentExportCapTotalKW) / 1000;

            // Game time
            m_TimeProvider ??= GameTimeSystem.Instance;
            dto.GameHour = m_TimeProvider?.Current.CurrentHour ?? Engine.Timing.DEFAULT_GAME_HOUR;

            // Grid stress
            if (m_StressDataQuery.TryGetSingleton<GridStressData>(out var stressData))
            {
                dto.StressPercent = stressData.StressPercent;
                dto.RecoveryHours = stressData.RecoveryHoursRemaining;
                dto.CollapseThresholdHours = stressData.CollapseThresholdHours;
                dto.GridFrequency = stressData.CurrentFrequency;
                dto.StressZone = ZoneToString(stressData.Zone);
            }
            else
            {
                dto.GridFrequency = NOMINAL_FREQUENCY_HZ;
                if (!m_StressDataWarned) { Log.Warn("[DIAG] PowerGridUI: GridStressData not found on first tick"); m_StressDataWarned = true; }
            }

            // Threshold
            int autoCutKW = 0;
            if (m_ThresholdQuery.TryGetSingleton<ThresholdStateSingleton>(out var threshold))
            {
                dto.ThresholdActive = threshold.IsActive;
                dto.BuildingsCutCount = threshold.CutoffCount;
                autoCutKW = threshold.CutoffKW;
            }

            // Power flow breakdown (for UI: Demand → Delivered ← ForcedOff).
            // Demand = total wanted; Consumption = scheduled active load (post-district
            // category toggles, pre-threshold). Delivered = Consumption − threshold cut.
            // ForcedOff = everything that didn't reach buildings = Demand − Delivered.
            // AutoDispatch shedding is already reflected in Consumption (categories OFF
            // produce 0 KW in DistrictPowerBuffer), so its delta vs Demand is captured
            // in DistrictShedMW. The dedicated AutoDispatchShedMW field is reserved for
            // future use when AutoDispatch reports a separate KW counter.
            var gridForBreakdown = m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var pgb)
                ? pgb : PowerGridSingleton.Default;
            int autoCutMW = autoCutKW / 1000;
            int districtShedMW = (gridForBreakdown.Demand - gridForBreakdown.Consumption) / 1000;
            if (districtShedMW < 0) districtShedMW = 0;
            // Delivered is bounded by actual supply, not just scheduled load. Production already
            // includes external imports. Without this cap, delivered tracked Consumption only and
            // stayed high even when production collapsed to 0 (grid down, yet UI showed full load
            // as "delivered" and the brownout colour meter never fired). delivered = min(supply, active load).
            int suppliedMW = gridForBreakdown.Production / 1000;
            int activeLoadMW = gridForBreakdown.Consumption / 1000 - autoCutMW;
            int deliveredMW = suppliedMW < activeLoadMW ? suppliedMW : activeLoadMW;
            if (deliveredMW < 0) deliveredMW = 0;
            int forcedOffMW = gridForBreakdown.Demand / 1000 - deliveredMW;
            if (forcedOffMW < 0) forcedOffMW = 0;

            dto.DeliveredMW = deliveredMW;
            dto.ForcedOffMW = forcedOffMW;
            dto.AutoCutMW = autoCutMW;
            dto.DistrictShedMW = districtShedMW;
            dto.AutoDispatchShedMW = 0;

            // City schedule
            dto.CitySchedule = (int)m_DistrictState.CitySchedule;
            dto.EffectiveCityMode = GetEffectiveCityMode(dto.CitySchedule);
            dto.DistrictsOverrideCity = HasDistrictOverrides(dto.CitySchedule);

            var cityScheduleGate = ActionGate.Resolve(ActionKey.CitySchedule, BuildActionContext());
            dto.CityScheduleAvailability = cityScheduleGate;

            // Auto dispatch
            UpdateAutoDispatch(ref dto);

            long shadowBalance = 0;
            long shadowPending = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).PendingDeductions;
            float shadowMarkup = 0f;
            bool shadowFrozen = true;
            if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
            {
                dto.ShadowBalance = (int)Math.Clamp(wallet.Balance, int.MinValue, int.MaxValue);
                shadowBalance = wallet.Balance;
                shadowMarkup = wallet.SanctionsMarkup;
                shadowFrozen = wallet.IsFrozen;
            }
            // Serialize generation sources as JSON array for PowerGridDto (skip when data unchanged)
            int plantsVersion = m_WearUIService.PlantsVersion;
            var repairIntentView = m_PlantRepairIntentReader.RepairIntentView;
            var repairIntentSnapshot = repairIntentView?.Observe(ref m_RepairIntentObserverCursor)
                ?? new ViewSnapshot<PlantRepairIntentSnapshot>(PlantRepairIntentSnapshot.Empty, 0, false);
            int repairIntentSnapshotVersion = repairIntentSnapshot.Version;
            int plantRepairPhase = (int)(m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws)
                    ? ws : WaveStateSingleton.Default)
                .CurrentPhase;
            long cityBudgetBalance = CityBudgetService.GetBalance(World);
            long cityBudgetPending = CityBudgetService.PendingDeductions;
            int powerDomainVersion = GetPowerDomainDirtyVersion();
            m_GenerationSourcesCacheInputView.Publish(new GenerationSourcesCacheInput(
                plantsVersion,
                repairIntentSnapshotVersion,
                plantRepairPhase,
                cityBudgetBalance,
                cityBudgetPending,
                powerDomainVersion,
                shadowBalance,
                shadowPending,
                shadowMarkup,
                shadowFrozen));
            if (m_GenerationSourcesCacheInputView.Observe(ref m_GenerationSourcesInputCursor).Changed)
            {
                m_LastGenerationSourcesJson = SerializePlants();
            }
            dto.GenerationSourcesJson = m_LastGenerationSourcesJson;
            dto.AtRiskPlantCount = m_LastAtRiskPlantCount;

            var repairCfg = balanceCfg.InfrastructureRepair;

            // Civilian damage data (skip re-serialization when data unchanged)
            var damageView = m_CivilianDamageReader.DamageView;
            var damageSnapshot = damageView?.Observe(ref m_CivilianDamageSnapshotCursor)
                ?? new ViewSnapshot<CivilianDamageSnapshot>(CivilianDamageSnapshot.Empty, 0, false);
            m_CurrentCivilianDamageSnapshot = damageSnapshot.Value;
            int damageSnapshotVersion = damageSnapshot.Version;
            int civilianRepairPhase = plantRepairPhase;
            long civilianBudgetBalance = cityBudgetBalance;
            long civilianBudgetPending = cityBudgetPending;
            m_CivilianDamageCacheInputView.Publish(new CivilianDamageCacheInput(
                damageSnapshotVersion,
                civilianRepairPhase,
                civilianBudgetBalance,
                civilianBudgetPending,
                powerDomainVersion,
                shadowBalance,
                shadowPending,
                shadowMarkup,
                shadowFrozen));
            if (m_CivilianDamageCacheInputView.Observe(ref m_CivilianDamageInputCursor).Changed)
            {
                m_LastCivilianDamageJson = SerializeCivilianDamage();
            }
            dto.CivilianDamageJson = m_LastCivilianDamageJson;
            dto.PlantMunicipalRepairHours = repairCfg.MunicipalRepairHours;
            dto.PlantShadowOpsRepairHours = repairCfg.ShadowOpsRepairHours;
            dto.CivilianMunicipalRepairHours = repairCfg.CivilianMunicipalRepairHours;
            dto.CivilianShadowOpsRepairHours = repairCfg.CivilianShadowOpsRepairHours;
            dto.PlantRepairRequestJson = RequestResultBridge.Get(RequestResultBridge.PlantRepair).ToJson();
            dto.CivilianRepairRequestJson = RequestResultBridge.Get(RequestResultBridge.CivilianRepair).ToJson();
            dto.AutoDispatchToggleRequestJson = RequestResultBridge.Get(RequestResultBridge.AutoDispatchToggle).ToJson();
            dto.DistrictToggleRequestJson = RequestResultBridge.Get(RequestResultBridge.DistrictToggle).ToJson();
            dto.CitySchedulePeriodRequestJson = RequestResultBridge.Get(RequestResultBridge.CitySchedule).ToJson();
            dto.DistrictInternetToggleRequestJson = RequestResultBridge.Get(RequestResultBridge.DistrictInternetToggle).ToJson();
            PublishWhenComplete(PowerGridState, NoSourceChecks, () => dto);
        }

        private int GetPowerDomainDirtyVersion()
        {
            var hash = new HashCode();
#pragma warning disable CIVIC005 // Dirty-key owner readers may be absent during startup; neutral 0 keeps cache conservative.
            hash.Add(m_ImportCapVersionReader.ImportCapView?.Observe(ref m_ImportCapSnapshotCursor).Version ?? 0);
            hash.Add(m_ServedLoadVersionReader.ServedLoadView?.Observe(ref m_ServedLoadSnapshotCursor).Version ?? 0);
            hash.Add(m_BlackoutStateVersionReader.BlackoutStateView?.Observe(ref m_BlackoutStateSnapshotCursor).Version ?? 0);
            hash.Add(m_AutoDispatchVersionReader.AutoDispatchOwnershipView?.Observe(ref m_AutoDispatchOwnershipSnapshotCursor).Version ?? 0);
            hash.Add(m_CollapseOwnerVersionReader.CollapseOwnerView?.Observe(ref m_CollapseOwnerSnapshotCursor).Version ?? 0);
#pragma warning restore CIVIC005
            return hash.ToHashCode();
        }

        private TriggerOutcome OnSetCitySchedule(int scheduleId)
        {
            if (scheduleId < 0 || scheduleId > (int)SchedulePreset.DayShift)
            {
                Log.Warn($"OnSetCitySchedule: invalid scheduleId={scheduleId}");
                return TriggerOutcome.Reject(ReasonIds.GridInvalidSchedule);
            }
            m_DistrictStateWriter.CitySchedule = (SchedulePreset)scheduleId;
            Log.Info($"City schedule set to: {(SchedulePreset)scheduleId}");
            return TriggerOutcome.SyncSuccess();
        }

        private ActionContext BuildActionContext()
        {
            // NO_MIGRATE: action context exposes actual WaveState presence.
            bool hasWaveState = m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState);
            return new ActionContext(
                hasWaveState,
                hasWaveState ? waveState.CurrentPhase : GamePhase.Calm,
                false,
                Act.PreWar);
        }

        private void UpdateAutoDispatch(ref PowerGridDto dto)
        {
            if (!m_AutoDispatchQuery.TryGetSingleton<AutoDispatchData>(out var data))
                return;

            dto.AutoDispatchEnabled = data.Enabled;
            dto.AutoDispatchSheddedCount = data.AutoSheddedCount;
            dto.AutoDispatchBlockedByVip = data.IsBlockedByVip;
        }

        private int GetEffectiveCityMode(int citySchedule)
        {
            if (m_DistrictState == null)
                return citySchedule;

            if (!m_DistrictBufferQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity)
                || !World.EntityManager.HasBuffer<DistrictEntityEntry>(singletonEntity))
                return citySchedule;

            var snapshot = m_DistrictState.TakeSnapshot();
            var districts = World.EntityManager.GetBuffer<DistrictEntityEntry>(singletonEntity, true);
            bool hasEligibleDistrict = false;
            for (int i = 0; i < districts.Length; i++)
            {
                var district = districts[i].GetEntity();
                if (!World.EntityManager.Exists(district) || snapshot.IsVIP(district.Index))
                    continue;

                hasEligibleDistrict = true;
                if (!IsFullManualBlackout(snapshot, district.Index))
                    return citySchedule;
            }

            return hasEligibleDistrict ? -1 : citySchedule;
        }

        private static bool IsFullManualBlackout(in DistrictStateSnapshot snapshot, int districtIndex)
        {
            if (!snapshot.DistrictBlackouts.TryGetValue(districtIndex, out var categories))
                return false;

            foreach (var category in BuildingCategories.All)
            {
                if (!categories.Contains(category))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// True when at least one non-VIP district diverges from the city preset — either an
        /// explicit per-district schedule different from the city schedule, or any category
        /// blackout. The city label keeps showing the real city setting; this drives the UI
        /// override marker so the player sees that districts are managed individually. VIP
        /// districts are excluded (they carry their own indicator).
        /// </summary>
        private bool HasDistrictOverrides(int citySchedule)
        {
            if (m_DistrictState == null)
                return false;

            if (!m_DistrictBufferQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity)
                || !World.EntityManager.HasBuffer<DistrictEntityEntry>(singletonEntity))
                return false;

            var citySchedulePreset = (SchedulePreset)citySchedule;
            var snapshot = m_DistrictState.TakeSnapshot();
            var districts = World.EntityManager.GetBuffer<DistrictEntityEntry>(singletonEntity, true);
            for (int i = 0; i < districts.Length; i++)
            {
                var district = districts[i].GetEntity();
                if (!World.EntityManager.Exists(district) || snapshot.IsVIP(district.Index))
                    continue;

                if (DistrictDivergesFromCity(snapshot, district.Index, citySchedulePreset))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A non-VIP district diverges from the city preset when it carries an explicit
        /// schedule different from the city schedule, or any category blackout. Takes the
        /// logical district index as an int (snapshot keys are logical district ids, not raw
        /// entity refs).
        /// </summary>
        private static bool DistrictDivergesFromCity(in DistrictStateSnapshot snapshot, int districtIndex, SchedulePreset citySchedule)
        {
            if (snapshot.DistrictSchedules.TryGetValue(districtIndex, out var sched) && sched != citySchedule)
                return true;
            return snapshot.DistrictBlackouts.TryGetValue(districtIndex, out var cats) && cats.Count > 0;
        }

        private TriggerOutcome OnToggleAutoDispatch()
        {
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new AutoDispatchToggleRequest());
            Log.Info("Created AutoDispatchToggleRequest");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private string SerializePlants()
        {
            var plants = m_WearUIService.GetPlantsSnapshot();
            if (plants.Count == 0)
            {
                m_LastAtRiskPlantCount = 0;
                return JsonBuilder.EmptyArray;
            }

            var sb = new StringBuilder(PLANTS_JSON_INITIAL_CAPACITY);
            sb.Append('[');
            bool first = true;
            int atRiskPlantCount = 0;
            var currentPhase = (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws)
                    ? ws : WaveStateSingleton.Default)
                .CurrentPhase;
            for (int i = 0; i < plants.Count; i++)
            {
                var p = plants[i];
                bool isAtRisk = IsPlantAtRisk(p);
                if (isAtRisk)
                    atRiskPlantCount++;
                var repairCfg = BalanceConfig.Current.InfrastructureRepair;
                // RepairBillablePercent already carries the max of all four damage sources
                // (wear/explosion/operational/disaster) collected in EquipmentUISystem; the shared
                // helper applies the same clamp the repair-transaction layer uses, so the UI charge
                // and the billed charge stay on one formula.
                // Destroyed (vanilla-knocked-out ruin) is not mod-repairable — zero the billable
                // percent so every charge is 0 and ForPlantRepair rejects (CanRun=false). The UI
                // routes IsDestroyed rows into the collapsed "destroyed" group, not the repair flow.
                int billablePercent = p.IsDestroyed ? 0 : RepairPaymentHelper.BillableRepairPercent(
                    p.RepairBillablePercent, 0f, 0f, 0f);
                bool hasPendingRepairIntent = m_PlantRepairIntentReader.HasPendingRepairIntent(p.PlantId);
                int municipalRepairCharge = billablePercent * repairCfg.MunicipalBaseCostPerPercent;
                int municipalKickbackRepairCharge = (int)Math.Round(municipalRepairCharge * repairCfg.MunicipalCostMultiplierWithKickback);
                int kickbackRepairAmount = (int)Math.Round(municipalKickbackRepairCharge * repairCfg.MunicipalKickbackPercent);
                var municipalRepair = PlantRepairEligibility.ForPlantRepair(
                    currentPhase,
                    hasPendingRepair: hasPendingRepairIntent,
                    foundPlant: true,
                    canApplyRepairState: true,
                    isUnderRepair: p.IsRepairing,
                    billableRepairPercent: billablePercent,
                    repairType: RepairType.Municipal,
                    world: World);
                var kickbackRepair = PlantRepairEligibility.ForPlantRepair(
                    currentPhase,
                    hasPendingRepair: hasPendingRepairIntent,
                    foundPlant: true,
                    canApplyRepairState: true,
                    isUnderRepair: p.IsRepairing,
                    billableRepairPercent: billablePercent,
                    repairType: RepairType.MunicipalWithKickback,
                    world: World);
                int shadowBaseCost = billablePercent * repairCfg.ShadowOpsBaseCostPerPercent;
                var shadowRepair = PlantRepairEligibility.ForPlantRepair(
                    currentPhase,
                    hasPendingRepair: hasPendingRepairIntent,
                    foundPlant: true,
                    canApplyRepairState: true,
                    isUnderRepair: p.IsRepairing,
                    billableRepairPercent: billablePercent,
                    repairType: RepairType.ShadowOps,
                    world: World);
                int shadowOpsRepairCharge = CalculateShadowRepairCharge(shadowBaseCost);

                var entry = new PlantWearData
                {
                    PlantId = p.PlantId,
                    Name = p.Name ?? string.Empty,
                    CapacityMW = p.CapacityMW,
                    CurrentOutputMW = p.CurrentOutputMW,
                    WearPercent = p.WearPercent,
                    RepairBillablePercent = p.RepairBillablePercent,
                    IsRepairable = p.IsRepairable,
                    IsDestroyed = p.IsDestroyed,
                    IsRepairing = p.IsRepairing,
                    RepairHoursLeft = p.RepairHoursLeft,
                    HasExploded = p.HasExploded,
                    IsUnderConstruction = p.IsUnderConstruction,
                    ConstructionDaysLeft = p.ConstructionDaysLeft,
                    OperationalDamagePercent = p.OperationalDamagePercent,
                    OperationalHitCount = p.OperationalHitCount,
                    OperationalHitMax = p.OperationalHitMax,
                    DisasterDamagePercent = p.DisasterDamagePercent,
                    IsAtRisk = isAtRisk,
                    MunicipalRepairCharge = municipalRepairCharge,
                    MunicipalKickbackRepairCharge = municipalKickbackRepairCharge,
                    KickbackRepairAmount = kickbackRepairAmount,
                    CanMunicipalRepair = municipalRepair.CanRun,
                    MunicipalRepairLockedReasonId = municipalRepair.LockedReasonId,
                    CanKickbackRepair = kickbackRepair.CanRun,
                    KickbackRepairLockedReasonId = kickbackRepair.LockedReasonId,
                    ShadowOpsRepairCharge = shadowOpsRepairCharge,
                    CanShadowRepair = shadowRepair.CanRun,
                    ShadowRepairLockedReasonId = shadowRepair.LockedReasonId,
                    State = p.State,
                    SaturationFactor = p.SaturationFactor,
                    FuelAvailabilityPercent = p.FuelAvailabilityPercent,
                    FuelFactor = p.FuelFactor,
                    RecoveryHours = p.RecoveryHours,
                };
                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }
            m_LastAtRiskPlantCount = atRiskPlantCount;
            sb.Append(']');
            return sb.ToString();
        }

        private static bool IsPlantAtRisk(PlantWearProducerData plant)
        {
            return !plant.IsRepairing
                && !plant.IsDestroyed   // a destroyed ruin is in the "Destroyed" group, not actionable at-risk
                && (EquipmentWearUtils.IsInDangerZone(plant.WearPercent)
                    || plant.OperationalDamagePercent > 0f
                    || plant.DisasterDamagePercent > 0f);
        }

        private TriggerOutcome OnRepairPlant(in ScenarioGuard guard, int plantId, int repairTypeInt)
        {
            if (repairTypeInt < 0 || repairTypeInt > 2)
            {
                Log.Warn($"Invalid repairType {repairTypeInt}");
                return TriggerOutcome.Reject(ReasonIds.InvalidRepairType);
            }

            var repairType = repairTypeInt switch
            {
                0 => RepairType.Municipal,
                1 => RepairType.MunicipalWithKickback,
                2 => RepairType.ShadowOps,
                _ => throw new System.InvalidOperationException($"Unreachable: repairTypeInt={repairTypeInt}")
            };

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PlantRepairRequest
            {
                StablePlantId = plantId,
                RepairType = repairType
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created PlantRepairRequest: plantId={plantId}, repairType={repairType}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private string SerializeCivilianDamage()
        {
            var buildings = m_CurrentCivilianDamageSnapshot.Buildings;
            if (buildings.Count == 0)
                return JsonBuilder.EmptyArray;

            m_EntityStorageInfoLookup.Update(this);
            var sb = new StringBuilder(CIVILIAN_DAMAGE_JSON_INITIAL_CAPACITY);
            sb.Append('[');
            bool first = true;
            string waveReasonId = "";
            // NO_MIGRATE: phase gating is conditional on actual WaveState presence.
            bool waveBlocked = m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState)
                && !waveState.TryRequirePhaseSafe(out waveReasonId);
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                var buildingEntity = b.Building.ToEntity();
                if (!m_EntityStorageInfoLookup.Exists(buildingEntity)) continue;
                string name = m_NameSystem?.GetRenderedLabelName(buildingEntity) ?? "";
                var repairCfg = BalanceConfig.Current.InfrastructureRepair;
                int municipalRepairCharge = b.HitCount * repairCfg.CivilianMunicipalCostPerHit;
                int municipalKickbackRepairCharge = (int)Math.Round(municipalRepairCharge * repairCfg.MunicipalCostMultiplierWithKickback);
                int kickbackRepairAmount = (int)Math.Round(municipalKickbackRepairCharge * repairCfg.MunicipalKickbackPercent);
                var municipalRepair = CivilianRepairEligibility.ForCivilianRepair(
                    waveBlocked, waveReasonId, b.HitCount, RepairType.Municipal, World);
                var kickbackRepair = CivilianRepairEligibility.ForCivilianRepair(
                    waveBlocked, waveReasonId, b.HitCount, RepairType.MunicipalWithKickback, World);
                int shadowBaseCost = b.HitCount * repairCfg.CivilianShadowOpsCostPerHit;
                var shadowRepair = CivilianRepairEligibility.ForCivilianRepair(
                    waveBlocked, waveReasonId, b.HitCount, RepairType.ShadowOps, World);
                int shadowOpsRepairCharge = CalculateShadowRepairCharge(shadowBaseCost);

                var entry = new CivilianDamageData
                {
                    Building = new EntityRefDto(b.Building.Index, b.Building.Version),
                    Name = name,
                    HitCount = b.HitCount,
                    MaxHits = b.MaxHits,
                    DamagePercent = b.DamagePercent,
                    IsRepairing = b.IsRepairing,
                    RepairHoursLeft = b.RepairHoursLeft,
                    MunicipalRepairCharge = municipalRepairCharge,
                    MunicipalKickbackRepairCharge = municipalKickbackRepairCharge,
                    KickbackRepairAmount = kickbackRepairAmount,
                    CanMunicipalRepair = municipalRepair.CanRun,
                    MunicipalRepairLockedReasonId = municipalRepair.LockedReasonId,
                    CanKickbackRepair = kickbackRepair.CanRun,
                    KickbackRepairLockedReasonId = kickbackRepair.LockedReasonId,
                    ShadowOpsRepairCharge = shadowOpsRepairCharge,
                    CanShadowRepair = shadowRepair.CanRun,
                    ShadowRepairLockedReasonId = shadowRepair.LockedReasonId,
                };
                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private int CalculateShadowRepairCharge(int baseCost)
        {
            if (baseCost <= 0)
                return 0;

            float markup = (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var sw)
                ? sw : ShadowWalletSingleton.Default).SanctionsMarkup;
            return Math.Clamp(SanctionsCostHelper.ApplyMarkup(baseCost, markup), 0, int.MaxValue);
        }

        private readonly struct GenerationSourcesCacheInput : IEquatable<GenerationSourcesCacheInput>
        {
            private readonly int m_PlantsVersion;
            private readonly int m_RepairIntentStamp;
            private readonly int m_PlantRepairPhase;
            private readonly long m_CityBudgetBalance;
            private readonly long m_CityBudgetPending;
            private readonly int m_PowerDomainVersion;
            private readonly long m_ShadowBalance;
            private readonly long m_ShadowPending;
            private readonly float m_ShadowMarkup;
            private readonly bool m_ShadowFrozen;

            public GenerationSourcesCacheInput(
                int plantsVersion,
                int repairIntentSnapshotVersion,
                int plantRepairPhase,
                long cityBudgetBalance,
                long cityBudgetPending,
                int powerDomainVersion,
                long shadowBalance,
                long shadowPending,
                float shadowMarkup,
                bool shadowFrozen)
            {
                m_PlantsVersion = plantsVersion;
                m_RepairIntentStamp = repairIntentSnapshotVersion;
                m_PlantRepairPhase = plantRepairPhase;
                m_CityBudgetBalance = cityBudgetBalance;
                m_CityBudgetPending = cityBudgetPending;
                m_PowerDomainVersion = powerDomainVersion;
                m_ShadowBalance = shadowBalance;
                m_ShadowPending = shadowPending;
                m_ShadowMarkup = shadowMarkup;
                m_ShadowFrozen = shadowFrozen;
            }

            public bool Equals(GenerationSourcesCacheInput other)
                => m_PlantsVersion == other.m_PlantsVersion
                    && m_RepairIntentStamp == other.m_RepairIntentStamp
                    && m_PlantRepairPhase == other.m_PlantRepairPhase
                    && m_CityBudgetBalance == other.m_CityBudgetBalance
                    && m_CityBudgetPending == other.m_CityBudgetPending
                    && m_PowerDomainVersion == other.m_PowerDomainVersion
                    && m_ShadowBalance == other.m_ShadowBalance
                    && m_ShadowPending == other.m_ShadowPending
                    && Math.Abs(m_ShadowMarkup - other.m_ShadowMarkup) <= SHADOW_MARKUP_EPSILON
                    && m_ShadowFrozen == other.m_ShadowFrozen;

            public override bool Equals(object obj)
                => obj is GenerationSourcesCacheInput other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    m_PlantsVersion,
                    m_RepairIntentStamp,
                    m_PlantRepairPhase,
                    m_CityBudgetBalance,
                    m_CityBudgetPending,
                    m_PowerDomainVersion,
                    m_ShadowBalance,
                    m_ShadowPending);
        }

        private readonly struct CivilianDamageCacheInput : IEquatable<CivilianDamageCacheInput>
        {
            private readonly int m_DamageStamp;
            private readonly int m_CivilianRepairPhase;
            private readonly long m_CivilianBudgetBalance;
            private readonly long m_CivilianBudgetPending;
            private readonly int m_PowerDomainVersion;
            private readonly long m_ShadowBalance;
            private readonly long m_ShadowPending;
            private readonly float m_ShadowMarkup;
            private readonly bool m_ShadowFrozen;

            public CivilianDamageCacheInput(
                int damageSnapshotVersion,
                int civilianRepairPhase,
                long civilianBudgetBalance,
                long civilianBudgetPending,
                int powerDomainVersion,
                long shadowBalance,
                long shadowPending,
                float shadowMarkup,
                bool shadowFrozen)
            {
                m_DamageStamp = damageSnapshotVersion;
                m_CivilianRepairPhase = civilianRepairPhase;
                m_CivilianBudgetBalance = civilianBudgetBalance;
                m_CivilianBudgetPending = civilianBudgetPending;
                m_PowerDomainVersion = powerDomainVersion;
                m_ShadowBalance = shadowBalance;
                m_ShadowPending = shadowPending;
                m_ShadowMarkup = shadowMarkup;
                m_ShadowFrozen = shadowFrozen;
            }

            public bool Equals(CivilianDamageCacheInput other)
                => m_DamageStamp == other.m_DamageStamp
                    && m_CivilianRepairPhase == other.m_CivilianRepairPhase
                    && m_CivilianBudgetBalance == other.m_CivilianBudgetBalance
                    && m_CivilianBudgetPending == other.m_CivilianBudgetPending
                    && m_PowerDomainVersion == other.m_PowerDomainVersion
                    && m_ShadowBalance == other.m_ShadowBalance
                    && m_ShadowPending == other.m_ShadowPending
                    && Math.Abs(m_ShadowMarkup - other.m_ShadowMarkup) <= SHADOW_MARKUP_EPSILON
                    && m_ShadowFrozen == other.m_ShadowFrozen;

            public override bool Equals(object obj)
                => obj is CivilianDamageCacheInput other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    m_DamageStamp,
                    m_CivilianRepairPhase,
                    m_CivilianBudgetBalance,
                    m_CivilianBudgetPending,
                    m_PowerDomainVersion,
                    m_ShadowBalance,
                    m_ShadowPending,
                    m_ShadowFrozen);
        }

        private TriggerOutcome OnRepairCivilian(in ScenarioGuard guard, EntityRef building, int repairTypeInt)
        {
            if (repairTypeInt < 0 || repairTypeInt > 2)
            {
                Log.Warn($"Invalid civilian repairType {repairTypeInt}");
                return TriggerOutcome.Reject(ReasonIds.InvalidRepairType);
            }

            var repairType = repairTypeInt switch
            {
                0 => RepairType.Municipal,
                1 => RepairType.MunicipalWithKickback,
                2 => RepairType.ShadowOps,
                _ => throw new System.InvalidOperationException($"Unreachable: repairTypeInt={repairTypeInt}")
            };

            if (!building.TryResolve(World.EntityManager, out _))
            {
                Log.Warn($"OnRepairCivilian: stale building ref {building.Index} v{building.Version}, ignoring");
                return TriggerOutcome.Reject(ReasonIds.CivilianRepairNotFound);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new CivilianRepairRequest
            {
                Building = new BuildingRef(building.Index, building.Version),
                RepairType = repairType
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created CivilianRepairRequest: building={building.Index} v{building.Version}, repairType={repairType}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private static string ZoneToString(GridStressZone zone)
        {
            return zone switch
            {
                GridStressZone.Normal => "normal",
                GridStressZone.Yellow => "yellow",
                GridStressZone.Red => "red",
                GridStressZone.Collapsed => "collapsed",
                _ => "normal"
            };
        }
    }
}
