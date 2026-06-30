using System;
using System.Collections.Generic;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Adapters;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.PowerGrid.UI
{
    /// <summary>
    /// UI system for district management.
    /// Reads district power data from ECS singleton buffer.
    ///
    /// Migrated from DistrictUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// Removes #pragma warning disable CIVIC051 (now proper ECS system).
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.DistrictToggle)]
    public partial class DistrictUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private NameSystemFacade? m_NameService;
        private IDistrictStateReader? m_StateReader;
        private IDistrictStateWriter? m_StateWriter;

        // ECS queries
        private EntityQuery m_SingletonQuery;
        private EntityQuery m_SpotterCmQuery;
        private EntityQuery m_ThresholdQuery;
        private EntityQuery m_PowerGridQuery;

        // State
        private readonly List<DistrictDto> m_DistrictsList = new();

        private bool m_StateReaderWarned;
        // GC-FIX: Reusable lookups — keyed by DistrictRef.Packed
        private readonly Dictionary<long, DistrictPowerData> m_PowerLookup = new();
        [NonEntityIndex] private readonly HashSet<int> m_InternetDisabledDistricts = new();
        [NonEntityIndex] private readonly Dictionary<long, int> m_ThresholdCutLookup = new();
        [NonEntityIndex] private readonly Dictionary<long, int> m_ThresholdCutKWLookup = new();
        private Func<int, int, int>? m_GetThresholdCutCached;
        private Func<int, int, int>? m_GetThresholdCutKWCached;

        // PERF: Cache district names
        private readonly Dictionary<long, string> m_NameCache = new();
        private int m_NameCacheRefreshCounter = 0;
        private const int NAME_CACHE_REFRESH_INTERVAL = 300;

        // Upper bound guard against encoding overflow (districtIndex * 100 > int.MaxValue)
        private const int MAX_ENTITY_INDEX = int.MaxValue / 100;

        // PERF: Skip rebuild when data unchanged
        private int m_LastPowerFrame = -1;
        private int m_LastInternetLen = -1;
        private int m_LastThresholdSignature = int.MinValue;
        private bool m_TriggerDirty;

        // PERF D3: Delta detection — sort by load before serializing the list
        private static readonly IComparer<DistrictDto> s_TotalMWDescComparer =
            Comparer<DistrictDto>.Create((a, b) => b.TotalMW.CompareTo(a.TotalMW));

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            m_SingletonQuery = GetEntityQuery(
                ComponentType.ReadOnly<DistrictPowerBufferSingleton>());
            m_SpotterCmQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterCountermeasuresState>());
            m_ThresholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<ThresholdStateSingleton>());
            m_PowerGridQuery = GetEntityQuery(
                ComponentType.ReadOnly<PowerGridSingleton>());
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_NameService = ServiceRegistry.Instance.Require<NameSystemFacade>();
            m_StateReader = ServiceRegistry.Instance.Require<IDistrictStateReader>();
            m_StateWriter = ServiceRegistry.Instance.Require<IDistrictStateWriter>();
            m_TriggerDirty = true; // Force rebuild after load (dirty check counters may match stale values)
        }

        protected override void ConfigureBindings()
        {
            // Unified string-binding pipeline: payload is a JSON array string
            // (parsed on the UI by useSafeJsonArray). BindingRegistry dedups
            // identical strings, so rebuilding every tick is cheap when unchanged.
            Bindings.Add<string>(Districts, JsonBuilder.EmptyArray);
        }

        protected override void ConfigureTriggers()
        {
            // District blackout/category/schedule/VIP controls are pause-safe UI state
            // commands. Apply them synchronously through IDistrictStateWriter instead
            // of deferring to a simulation tick, so the grid panel reflects the click
            // while the game is paused.
            Triggers.Add<int>(ToggleDistrictBlackout, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnToggleDistrictBlackout);
            Triggers.Add<int, int>(SetDistrictBlackout, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnSetDistrictBlackout);
            Triggers.Add<int>(ToggleDistrictCategory, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnToggleDistrictCategory);
            Triggers.Add<int, int>(SetDistrictSchedule, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnSetDistrictSchedule);
            Triggers.AddWarTrigger<int>(ToggleVIP, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnToggleVIP);
            Triggers.AddWarTrigger<int>(ToggleVIPBypass, FeatureIds.PowerGrid, RequestResultBridge.DistrictToggle, OnToggleVIPBypass);

            // Internet is owned by the Spotters aggregate buffer, so it remains a
            // cross-domain request until that owner exposes an equivalent sync API.
            Triggers.Add<int>(ToggleInternet, FeatureIds.PowerGrid, RequestResultBridge.DistrictInternetToggle, OnToggleInternet);
        }

        protected override void OnPanelUpdate()
        {
            if (!m_SingletonQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity))
                return;
            if (!World.EntityManager.HasBuffer<DistrictPowerEntry>(singletonEntity)) return;
            if (!World.EntityManager.HasBuffer<DistrictEntityEntry>(singletonEntity)) return;
            var powerBuffer = World.EntityManager.GetBuffer<DistrictPowerEntry>(singletonEntity, true);
            var entityBuffer = World.EntityManager.GetBuffer<DistrictEntityEntry>(singletonEntity, true);

            // Refresh name cache periodically (must tick even when skipping rebuild)
            bool refreshNames = ++m_NameCacheRefreshCounter >= NAME_CACHE_REFRESH_INTERVAL;
            if (refreshNames)
            {
                m_NameCacheRefreshCounter = 0;
                m_NameCache.Clear();
            }

            // Read internet buffer length for dirty check
            int internetLen = 0;
            bool hasCmEntity = m_SpotterCmQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var cmEntity)
                && World.EntityManager.HasBuffer<InternetDisabledBuffer>(cmEntity);
            if (hasCmEntity)
                internetLen = World.EntityManager.GetBuffer<InternetDisabledBuffer>(cmEntity, true).Length;

            int thresholdSignature = GetThresholdSignature();

            // Early-out: skip full rebuild when data unchanged
            var singleton = World.EntityManager.GetComponentData<DistrictPowerBufferSingleton>(singletonEntity);
            if (singleton.LastUpdateFrame == m_LastPowerFrame
                && internetLen == m_LastInternetLen
                && thresholdSignature == m_LastThresholdSignature
                && !refreshNames
                && !m_TriggerDirty)
                return;

            if (m_StateReader == null)
            {
                if (!m_StateReaderWarned)
                {
                    Log.Warn("[DistrictUISystem] StateReader unavailable");
                    m_StateReaderWarned = true;
                }
                m_TriggerDirty = true;
                return;
            }

            var snapshot = m_StateReader.TakeSnapshot();
            m_StateReaderWarned = false;
            m_LastPowerFrame = singleton.LastUpdateFrame;
            m_LastInternetLen = internetLen;
            m_LastThresholdSignature = thresholdSignature;
            m_TriggerDirty = false;

            // Build lookup for power data
            m_PowerLookup.Clear();
            for (int i = 0; i < powerBuffer.Length; i++)
            {
                var entry = powerBuffer[i];
                m_PowerLookup[entry.District.Packed] = entry.Data;
            }

            // Read internet-disabled districts from buffer
            m_InternetDisabledDistricts.Clear();
            if (hasCmEntity)
            {
                var internetBuffer = World.EntityManager.GetBuffer<InternetDisabledBuffer>(cmEntity, true);
                for (int i = 0; i < internetBuffer.Length; i++)
                {
                    m_InternetDisabledDistricts.Add(internetBuffer[i].DistrictIndex);
                }
            }

            // Read threshold cut counts and KW from buffer
            m_ThresholdCutLookup.Clear();
            m_ThresholdCutKWLookup.Clear();
            if (m_ThresholdQuery.TryGetSingletonEntity<ThresholdStateSingleton>(out var thresholdEntity)
                && World.EntityManager.HasBuffer<ThresholdCutBuffer>(thresholdEntity))
            {
                var thresholdBuffer = World.EntityManager.GetBuffer<ThresholdCutBuffer>(thresholdEntity, true);
                for (int i = 0; i < thresholdBuffer.Length; i++)
                {
                    var entry = thresholdBuffer[i];
                    long key = MakeThresholdKey(entry.District.Index, entry.District.Version);
                    m_ThresholdCutLookup[key] = entry.CutCount;
                    m_ThresholdCutKWLookup[key] = entry.CutKW;
                }
            }

            m_GetThresholdCutCached ??= (districtIndex, districtVersion) => m_ThresholdCutLookup.TryGetValue(MakeThresholdKey(districtIndex, districtVersion), out var count) ? count : 0;
            m_GetThresholdCutKWCached ??= (districtIndex, districtVersion) => m_ThresholdCutKWLookup.TryGetValue(MakeThresholdKey(districtIndex, districtVersion), out var kw) ? kw : 0;
            Func<int, int, int>? getThresholdCut = m_ThresholdCutLookup.Count > 0 ? m_GetThresholdCutCached : null;
            Func<int, int, int>? getThresholdCutKW = m_ThresholdCutKWLookup.Count > 0 ? m_GetThresholdCutKWCached : null;

            // City-wide delivery ratio for per-district fulfilled-load approximation.
            // Vanilla CS2 distributes production across ACTIVE consumers, so the denominator
            // is active Consumption (post-shed), not full Demand. Dividing by Demand would
            // under-credit active districts whenever some districts are blacked/shed
            // (Demand includes their wanted load, which draws no current).
            float cityDeliveryRatio = 1f;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid) && grid.Consumption > 0)
            {
                cityDeliveryRatio = Unity.Mathematics.math.saturate((float)grid.Production / grid.Consumption);
            }

            m_DistrictsList.Clear();

            // Add "No District" entry (index 0)
            m_PowerLookup.TryGetValue(new DistrictRef(0, 0).Packed, out var noDistrictPower);
            int noDistrictPriority = snapshot.GetPriority(0);
            m_DistrictsList.Add(DistrictDtoFactory.CreateFromSnapshot(
                in snapshot, 0, 0, "No District", noDistrictPriority, noDistrictPower, m_InternetDisabledDistricts, getThresholdCut, getThresholdCutKW, cityDeliveryRatio));

            // Iterate district entities from buffer
            for (int i = 0; i < entityBuffer.Length; i++)
            {
                var districtRef = entityBuffer[i].District;
                var entity = districtRef.ToEntity();

                if (!World.EntityManager.Exists(entity))
                    continue;

                long districtKey = districtRef.Packed;
                if (!m_NameCache.TryGetValue(districtKey, out string name))
                {
                    name = m_NameService?.GetRenderedLabelName(entity)!;
                    if (string.IsNullOrEmpty(name))
                        name = $"District {entity.Index}";
                    m_NameCache[districtKey] = name!;
                }

                m_PowerLookup.TryGetValue(districtKey, out var districtPower);
                int priority = snapshot.GetPriority(entity.Index);
                m_DistrictsList.Add(DistrictDtoFactory.CreateFromSnapshot(
                    in snapshot, entity.Index, entity.Version, name, priority, districtPower, m_InternetDisabledDistricts, getThresholdCut, getThresholdCutKW, cityDeliveryRatio));
            }

            // Sort real districts by TotalMW descending (skip index 0 = "No District", pinned at top)
            if (m_DistrictsList.Count > 1)
                m_DistrictsList.Sort(1, m_DistrictsList.Count - 1, s_TotalMWDescComparer);

            PublishJsonWhenComplete(Districts, NoSourceChecks, () => DistrictDtoFactory.SerializeList(m_DistrictsList));
        }

        private TriggerOutcome OnToggleDistrictBlackout(int districtIndex)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            m_TriggerDirty = true;
            if (Log.IsDebugEnabled) Log.Debug($"[DIAG] OnToggleDistrictBlackout called: districtIndex={districtIndex}");
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.Blackout);
        }

        private TriggerOutcome OnSetDistrictBlackout(int districtIndex, int blackedOut)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            m_TriggerDirty = true;
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.SetBlackout, blackedOut != 0 ? 1 : 0);
        }

        private TriggerOutcome OnToggleDistrictCategory(int combined)
        {
            int districtIndex = combined / Engine.PowerGrid.UI_DISTRICT_ENCODING_MULTIPLIER;
            int categoryId = combined % Engine.PowerGrid.UI_DISTRICT_ENCODING_MULTIPLIER;

            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX || categoryId < 1 || categoryId > 5)
                return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);

            m_TriggerDirty = true;
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.Category, categoryId);
        }

        private TriggerOutcome OnSetDistrictSchedule(int districtIndex, int scheduleId)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX || scheduleId < 0 || scheduleId > (int)SchedulePreset.DayShift)
                return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);

            m_TriggerDirty = true;
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.Schedule, scheduleId);
        }

        private TriggerOutcome OnToggleVIP(in ScenarioGuard guard, int districtIndex)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            m_TriggerDirty = true;
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.VIP);
        }

        private TriggerOutcome OnToggleVIPBypass(in ScenarioGuard guard, int districtIndex)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            m_TriggerDirty = true;
            return ApplyDistrictToggleImmediately(districtIndex, DistrictToggleType.VIPBypass);
        }

        private TriggerOutcome OnToggleInternet(int districtIndex)
        {
            if (districtIndex < 0 || districtIndex > MAX_ENTITY_INDEX) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            m_TriggerDirty = true;
            return CreateInternetToggleRequest(districtIndex);
        }

        private TriggerOutcome CreateInternetToggleRequest(int districtIndex)
        {
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new DistrictInternetToggleRequest
            {
                DistrictEntityIndex = districtIndex
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created DistrictInternetToggleRequest: district={districtIndex}");
            return TriggerOutcome.HandOffToEcs(
                ecb,
                entity,
                SystemAPI.Time.ElapsedTime,
                TriggerOutcome.CurrentSimulationFrame(World),
                "districtIndex",
                districtIndex.ToString());
        }

        /// <summary>
        /// Applies district grid controls synchronously from the UI trigger. Do not
        /// convert blackout/category/schedule/VIP back to a request entity drained
        /// by a later update: these controls are expected to react in pause just
        /// like the city schedule control.
        /// </summary>
        private TriggerOutcome ApplyDistrictToggleImmediately(int districtIndex, DistrictToggleType toggleType, int categoryIndex = 0)
        {
            var gate = ActionGate.Resolve(ActionKey.DistrictSchedule, BuildActionContext());
            if (!gate.CanRun)
                return TriggerOutcome.RejectRuntime(gate.LockedReasonId);

            if (m_StateWriter == null || m_StateReader == null)
                return TriggerOutcome.Reject(ReasonIds.InternalError);

            // Synchronous publication can re-enter subscribers before the UI callback
            // returns. Current subscribers are non-recursive, and EventBus cycle
            // detection guards accidental feedback loops.
            switch (toggleType)
            {
                case DistrictToggleType.Blackout:
                    m_StateWriter.ToggleDistrictBlackout(districtIndex);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    break;

                case DistrictToggleType.SetBlackout:
                    m_StateWriter.SetDistrictBlackout(districtIndex, categoryIndex != 0);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    break;

                case DistrictToggleType.Category:
                    // Pass a typed enum value to IsDefined: BuildingCategory's underlying
                    // type is byte, and Enum.IsDefined throws ArgumentException when the
                    // value's type (int) does not match the enum underlying type.
                    var category = (BuildingCategory)categoryIndex;
                    if (!Enum.IsDefined(typeof(BuildingCategory), category)
                        || category == BuildingCategory.None)
                        return TriggerOutcome.Reject(ReasonIds.DistrictToggleUnknownCategory);

                    m_StateWriter.ToggleDistrictCategory(districtIndex, category);
                    if (category == BuildingCategory.Residential)
                        EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    break;

                case DistrictToggleType.Schedule:
                    // Same typed-IsDefined guard as Category: stays correct even if
                    // SchedulePreset's underlying type ever narrows from int to byte.
                    var schedule = (SchedulePreset)categoryIndex;
                    if (!Enum.IsDefined(typeof(SchedulePreset), schedule))
                        return TriggerOutcome.Reject(ReasonIds.DistrictToggleUnknownSchedule);

                    m_StateWriter.SetDistrictSchedule(districtIndex, schedule);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    break;

                case DistrictToggleType.VIP:
                    if (!m_StateReader.IsVIP(districtIndex) && m_StateReader.IsVIPBypass(districtIndex))
                        m_StateWriter.ToggleVIPBypass(districtIndex);

                    m_StateWriter.ToggleVIP(districtIndex);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    if (m_StateReader.IsVIP(districtIndex))
                    {
                        EventBus?.SafePublish(new CorruptionNarrativeEvent(
                            CorruptionNarrativeEventType.VIPProtected,
                            Location: $"District {districtIndex}"), nameof(DistrictUISystem));
                    }
                    break;

                case DistrictToggleType.VIPBypass:
                    if (!m_StateReader.IsVIPBypass(districtIndex) && m_StateReader.IsVIP(districtIndex))
                        m_StateWriter.ToggleVIP(districtIndex);

                    m_StateWriter.ToggleVIPBypass(districtIndex);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtIndex), nameof(DistrictUISystem));
                    if (m_StateReader.IsVIPBypass(districtIndex))
                    {
                        EventBus?.SafePublish(new CorruptionNarrativeEvent(
                            CorruptionNarrativeEventType.VIPBypass), nameof(DistrictUISystem));
                    }
                    break;

                default:
                    return TriggerOutcome.Reject(ReasonIds.DistrictToggleUnknownType);
            }

            if (Log.IsDebugEnabled) Log.Debug($"Applied district control: district={districtIndex}, type={toggleType}");
            return TriggerOutcome.SyncSuccess(
                discriminatorKind: "districtIndex",
                discriminatorValue: districtIndex.ToString());
        }

        private ActionContext BuildActionContext()
        {
#pragma warning disable CIVIC070 // Same timing as old request processor: one-frame wave-state lag is acceptable for UI gating
            bool hasWaveState = WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState);
#pragma warning restore CIVIC070
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            return new ActionContext(
                hasWaveState,
                hasWaveState ? waveState.CurrentPhase : GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);
        }

        private static long MakeThresholdKey(int districtIndex, int districtVersion)
        {
            return ((long)districtIndex << 32) | (uint)districtVersion;
        }

        private int GetThresholdSignature()
        {
            if (!m_ThresholdQuery.TryGetSingletonEntity<ThresholdStateSingleton>(out var thresholdEntity))
                return 0;

            var threshold = World.EntityManager.GetComponentData<ThresholdStateSingleton>(thresholdEntity);
            var hash = new HashCode();
            hash.Add(threshold.IsActive);
            hash.Add(threshold.CutoffCount);
            hash.Add(threshold.CutoffKW);

            if (World.EntityManager.HasBuffer<ThresholdCutBuffer>(thresholdEntity))
            {
                var buffer = World.EntityManager.GetBuffer<ThresholdCutBuffer>(thresholdEntity, true);
                hash.Add(buffer.Length);
                for (int i = 0; i < buffer.Length; i++)
                {
                    hash.Add(buffer[i].District.Index);
                    hash.Add(buffer[i].District.Version);
                    hash.Add(buffer[i].CutCount);
                    hash.Add(buffer[i].CutKW);
                }
            }

            return hash.ToHashCode();
        }
    }
}
