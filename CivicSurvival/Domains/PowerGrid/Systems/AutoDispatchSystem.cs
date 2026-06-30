using Game;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Logging;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.PowerGrid.Systems
{
    /// <summary>
    /// Auto-Dispatch System - automatic load shedding during grid stress.
    /// Crude safety net: prevents the 24h GRID COLLAPSE by shedding load, but bluntly (worse than a
    /// human) so manual management stays worthwhile.
    ///
    /// PANIC mode (StressPercent >= GridStress.WarningThresholdYellow — the Yellow zone):
    /// - Sheds proportionally to the deficit (gap × SHED_MARGIN), multiple units per tick, no cooldown —
    ///   fights to the end until RawBalance recovers or nothing is left to shed.
    /// - Shed unit = a whole real district (straight to OFF), or one category of the Unzoned aggregate
    ///   (the undistricted city — shed per-category so a small gap doesn't black out everything).
    /// - Order: cheapest category first (Services → Office → Commercial → Residential → Industrial).
    /// - VIP protected in PANIC; overridden at CRITICAL (>= WarningThresholdRed) as last resort.
    ///
    /// RECOVERY mode (RawBalance >= 0, capacity headroom >= max(RecoveryHeadroomMinMW,
    /// RecoveryHeadroomFraction × Demand), StressPercent below RecoveryStressThreshold):
    /// - Restores auto-shedded districts one at a time, reverse order, after a stability window.
    /// - Steps: OFF → Q1 → original (preserves player intent).
    /// - Headroom is measured against plant CAPACITY (IPowerCapacitySnapshotReader.DispatchableMW),
    ///   not flow: vanilla generation is demand-following, so flow surplus never materially exceeds
    ///   zero — an absolute flow threshold (the old +100 MW) was unreachable and auto-shedded
    ///   districts stayed dark forever. "Can the fleet carry this district back?" is a capacity
    ///   question; "is the grid healthy right now?" stays on RawBalance >= 0.
    ///
    /// Recovery cooldown: 30 seconds between restores (shedding has no cooldown).
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(AutoDispatchData))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    public partial class AutoDispatchSystem : ThrottledSystemBase, IAutoDispatchVersionReader, IAutoDispatchSettingsWriter, ICivicSingletonOwner<AutoDispatchData>
    {
        private static readonly LogContext Log = new("AutoDispatch");

        // PANIC/CRITICAL thresholds come from BalanceConfig.GridStress.WarningThresholdYellow/Red —
        // auto-dispatch must enter PANIC exactly when the grid enters the Yellow zone and override
        // VIP exactly at Red, so the two consumers share one config key (crisis_model.py mirrors this).
        // Recovery thresholds (RecoveryStressThreshold, RecoveryHeadroomMinMW/Fraction) live there too.
        private const float COOLDOWN_SECONDS = 30f;
        // Crude over-shed: cover the deficit gap × this margin (autopilot cuts more than a careful human).
        private const float SHED_MARGIN = 1.20f;
        // BUG-PWR-028 FIX: Hysteresis stabilization - require stable conditions before restore
        private const float STABILITY_SECONDS_REQUIRED = 20f;
        private const int DEFAULT_CATEGORY_PRIORITY = 99;
        private float m_StabilitySeconds;
        private double m_NextAllowedDispatchSecond;

        /// <summary>Result of a ShedToTargetMW pass.</summary>
        private enum ShedResult
        {
            Success = 0,       // District was shed
            SuccessVipOverride,// VIP district was shed (CRITICAL mode)
            NoAction,          // No districts need shedding (all already off)
            BlockedByVip       // All remaining districts are VIP (PANIC mode only)
        }

        // Priority: lower = shed first (Services = non-critical, shed before Office)
        private static readonly Dictionary<BuildingCategory, int> CategoryPriority = new()
        {
            { BuildingCategory.Services, 0 },
            { BuildingCategory.Office, 1 },
            { BuildingCategory.Commercial, 2 },
            { BuildingCategory.Residential, 3 },
            { BuildingCategory.Industrial, 4 }
        };

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private EntityQuery m_AutoDispatchQuery;
        private EntityQuery m_StressDataQuery;
        private EntityQuery m_DistrictPowerQuery;
        private BufferLookup<DistrictPowerEntry> m_DistrictPowerEntryLookup;
        // Services (lazy init)
        private IDistrictStateReader? m_StateReader;
        private IDistrictStateWriter? m_StateWriter;
        private IAutoDispatchStateWriter? m_AutoDispatchWriter;
        private IPowerCapacitySnapshotReader? m_CapacitySnapshotReader;
        private bool m_ServiceWarningLogged;

        // GC-FIX: Reusable collections to avoid allocation in hot paths
        // Hybrid shed unit: a whole real district (Whole=true) OR one Unzoned category (Whole=false, Cat set).
        private readonly List<(int Index, BuildingCategory Cat, bool Whole, int Priority, int MW, bool IsVip)> m_ShedCandidates = new();
        private readonly List<(int Index, int Priority, int MW)> m_RestoreCandidates = new();
        [NonEntityIndex] private readonly Dictionary<int, DistrictPowerData> m_PowerLookup = new();
        private readonly VersionedView<AutoDispatchOwnershipSnapshot> m_AutoDispatchOwnershipView = new(AutoDispatchOwnershipSnapshot.Empty);
        public IVersionedView<AutoDispatchOwnershipSnapshot> AutoDispatchOwnershipView => m_AutoDispatchOwnershipView;

        // Tracks the auto-shedded district identity set across ticks so the
        // snapshot revision bumps on rotation (district A restored / B
        // auto-shedded in the same tick, count unchanged). Compared via
        // SetEquals against the previous tick; swap on diff.
        [NonEntityIndex] private readonly HashSet<int> m_CurAutoShedded = new();
        [NonEntityIndex] private readonly HashSet<int> m_PrevAutoShedded = new();
#pragma warning disable CIVIC241 // Revision is ephemeral — never persisted; resets/load rebases it monotonically.
        [System.NonSerialized] private int m_AutoDispatchRevision;
        [System.NonSerialized] private bool m_RestoreAutoSheddedAfterLoad;
#pragma warning restore CIVIC241

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info("AutoDispatchSystem created");
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IAutoDispatchVersionReader>(this);
                ServiceRegistry.Instance.Register<IAutoDispatchSettingsWriter>(this);
            }

            m_AutoDispatchQuery = GetEntityQuery(ComponentType.ReadWrite<AutoDispatchData>());
            m_StressDataQuery = GetEntityQuery(ComponentType.ReadOnly<GridStressData>());
            m_DistrictPowerQuery = GetEntityQuery(ComponentType.ReadOnly<DistrictPowerBufferSingleton>());
            m_DistrictPowerEntryLookup = GetBufferLookup<DistrictPowerEntry>(true);

            EnsureAutoDispatchSingleton();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureServices();
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IAutoDispatchVersionReader>(this);
                ServiceRegistry.Instance.Unregister<IAutoDispatchSettingsWriter>();
            }

            // FIX #120: Destroy singleton entity created in OnCreate
            if (m_AutoDispatchQuery.TryGetSingletonEntity<AutoDispatchData>(out var dispatchEntity))
                EntityManager.DestroyEntity(dispatchEntity);

            m_StateReader = null!;
            m_StateWriter = null!;
            m_AutoDispatchWriter = null!;
            base.OnDestroy();
        }

        public bool TryToggleAutoDispatch(out bool enabled, out List<int> restoredDistricts)
        {
            restoredDistricts = new List<int>();
            enabled = false;

            EnsureAutoDispatchSingleton();
            if (!m_AutoDispatchQuery.TryGetSingletonEntity<AutoDispatchData>(out var dispatchEntity))
                return false;

            var data = EntityManager.GetComponentData<AutoDispatchData>(dispatchEntity);
            data.Enabled = !data.Enabled;
            enabled = data.Enabled;

            if (!data.Enabled)
            {
                if (!EnsureServices())
                    return false;

                restoredDistricts = m_AutoDispatchWriter.RestoreAllAutoShedded();
                data.AutoSheddedCount = 0;
                data.IsBlockedByVip = false;
            }

            EntityManager.SetComponentData(dispatchEntity, data);
            ObserveAutoDispatchOwnedVersion(data);

            return true;
        }

        protected override void OnThrottledUpdate()
        {
            m_DistrictPowerEntryLookup.Update(this);
            EnsureAutoDispatchSingleton();

            if (!EnsureServices()) return;

            // TOCTOU FIX: Use TryGetSingletonEntity instead of IsEmpty + GetSingletonEntity
            if (!m_AutoDispatchQuery.TryGetSingletonEntity<AutoDispatchData>(out var dispatchEntity))
                return;
            var dispatchData = SystemAPI.GetComponent<AutoDispatchData>(dispatchEntity);

            if (!dispatchData.Enabled)
            {
                if (m_RestoreAutoSheddedAfterLoad && EnsureServices())
                {
                    // ORDER-INVARIANT: ThreadSafeDistrictState restores PreShedStates
                    // through BlackoutSystem serialization; disabled AutoDispatchData
                    // must reconcile that split state before the disabled early-return.
                    int restored = m_AutoDispatchWriter.RestoreAllAutoShedded().Count;
                    dispatchData.AutoSheddedCount = 0;
                    dispatchData.IsBlockedByVip = false;
                    m_RestoreAutoSheddedAfterLoad = false;
                    ObserveAutoDispatchOwnedVersion(dispatchData);
                    SystemAPI.SetComponent(dispatchEntity, dispatchData);
                    if (restored > 0)
                        Log.Info($"[AutoDispatch] Post-load restored {restored} auto-shedded district(s) while disabled");
                }
                return;
            }

            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_StressDataQuery.TryGetSingleton<GridStressData>(out var stressData))
                return;

            // Recovery metric from PowerGridSingleton.RawBalance (kW): delivered production flow
            // (Σ ElectricityProducer.m_LastProduction) minus active scheduled load. This converges
            // under shedding — shedding a district drops active Consumption, so RawBalance climbs and
            // RECOVERY can fire (pre-regression semantic, commit 4212a3e44). Regression a0efe1d79
            // sourced this from snapshot.DispatchableMW (plant CAPACITY), so RECOVERY fired on phantom
            // surplus while real delivery fell short. RawBalance is export-EXCLUDED: districts are
            // restored on physical surplus, and the cost of exporting while districts are dark is
            // charged as a morale penalty (§11), not by silently withholding power. Shadow export
            // cannot over-commit recovery: it is a monetary layer capped by capacity headroom
            // (PowerHeadroomMath) and never moves the physical flow; legal export is capacity-capped
            // at the trade edge, so a restored district is always physically served. See §10-M
            // (resolved via RawBalance).
            // PowerGridSingleton fields are KW; thresholds below are MW, so convert.
#pragma warning disable CIVIC070 // Power data changes gradually; 1-frame lag invisible for recovery hysteresis
            bool hasGridData = SystemAPI.TryGetSingleton<PowerGridSingleton>(out var powerGrid);
#pragma warning restore CIVIC070
            int realBalanceMW = hasGridData ? powerGrid.RawBalance / 1000 : 0;
            var gsCfg = BalanceConfig.Current.GridStress;

            // Sync count unconditionally (UI stays current even during cooldown / after load)
            int actualSheddedCount = m_StateReader.GetAutoSheddedCount();
            actualSheddedCount = math.clamp(actualSheddedCount, 0, AutoDispatchData.MAX_SHED_COUNT);
            dispatchData.AutoSheddedCount = actualSheddedCount;

            if (!hasGridData)
            {
                ObserveAutoDispatchOwnedVersion(dispatchData);
                SystemAPI.SetComponent(dispatchEntity, dispatchData);
                return;
            }

            bool hasPowerData = m_DistrictPowerQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var bufferEntity) &&
                                m_DistrictPowerEntryLookup.TryGetBuffer(bufferEntity, out var powerBuffer) &&
                                powerBuffer.Length > 0;

            // LOAD-INVARIANT: runtime ticks can precede GameTime activation after load/hot-reload.
            if (!GameTimeSystem.TryGetTotalGameSeconds(out var nowSeconds))
            {
                ObserveAutoDispatchOwnedVersion(dispatchData);
                SystemAPI.SetComponent(dispatchEntity, dispatchData);
                return;
            }
            // NOTE: no global cooldown gate here — shedding runs every tick to close the deficit (fight
            // to the end). The cooldown (m_NextAllowedDispatchSecond) now gates only recovery spacing.

            // PANIC or CRITICAL mode — same zone thresholds the grid itself uses
            if (stressData.StressPercent >= gsCfg.WarningThresholdYellow)
            {
                if (hasPowerData)
                {
                    m_StabilitySeconds = 0f; // Reset recovery timer — PANIC invalidates accumulated stability
                    bool isCritical = stressData.StressPercent >= gsCfg.WarningThresholdRed;
                    // gap = how far delivered flow falls short of active load (MW); over-shed by SHED_MARGIN.
                    int gapMW = realBalanceMW < 0 ? -realBalanceMW : 0;
                    int targetMW = (int)math.ceil(gapMW * SHED_MARGIN);
                    var shedResult = ShedToTargetMW(targetMW, isCritical);

#pragma warning disable CIVIC102 // None result is implicit no-op (not in if chain)
                    if (shedResult == ShedResult.Success || shedResult == ShedResult.SuccessVipOverride)
#pragma warning restore CIVIC102
                    {
                        actualSheddedCount = math.clamp(m_StateReader.GetAutoSheddedCount(), 0, AutoDispatchData.MAX_SHED_COUNT);
                        dispatchData.AutoSheddedCount = actualSheddedCount;
                        dispatchData.IsBlockedByVip = false;
                    }
                    else if (shedResult == ShedResult.BlockedByVip)
                    {
                        // BUG-INV-001 FIX: Warn when all districts are VIP (only in PANIC, not CRITICAL)
                        if (!dispatchData.IsBlockedByVip)
                        {
                            dispatchData.IsBlockedByVip = true;
                            Log.Warn("[AutoDispatch] BLOCKED: All districts are VIP - cannot shed load!");
                        }
                    }
                }
            }
            // Clear VIP blocked status when stress is below panic threshold
            else if (dispatchData.IsBlockedByVip)
            {
                dispatchData.IsBlockedByVip = false;
                Log.Info("[AutoDispatch] VIP block cleared - stress normalized");
            }
            else
            {
                // RECOVERY mode with BUG-PWR-028 FIX: Stability requirement prevents oscillation
                // M2 FIX: Require power buffer to be populated — prevents false surplus during ~1s
                // empty buffer window after load (DistrictPowerSystem needs 1 throttle tick to fill it).
                // Readiness = capacity headroom (can the fleet carry the district back?), gated by
                // RawBalance >= 0 (is delivery healthy right now?). A flow-only threshold is
                // unreachable under demand-following generation — see the class doc.
                m_CapacitySnapshotReader ??= ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
                int headroomMW = m_CapacitySnapshotReader.DispatchableMW
                    - powerGrid.Consumption / Engine.PowerGrid.KW_PER_MW;
                float requiredHeadroomMW = math.max(
                    (float)gsCfg.RecoveryHeadroomMinMW,
                    gsCfg.RecoveryHeadroomFraction * powerGrid.Demand / Engine.PowerGrid.KW_PER_MW);
                bool meetsRecoveryConditions = hasPowerData &&
                                               realBalanceMW >= 0 &&
                                               headroomMW >= requiredHeadroomMW &&
                                               stressData.StressPercent < gsCfg.RecoveryStressThreshold &&
                                               actualSheddedCount > 0;

                if (meetsRecoveryConditions)
                {
                    m_StabilitySeconds = math.min(STABILITY_SECONDS_REQUIRED, m_StabilitySeconds + ThrottledDeltaSeconds);

                    // Only restore after sustained stability period (prevents Hz oscillation) AND after the
                    // recovery cooldown (spaces restores; the shed path no longer uses this cooldown).
                    if (m_StabilitySeconds >= STABILITY_SECONDS_REQUIRED && nowSeconds >= m_NextAllowedDispatchSecond)
                    {
                        if (TryRestoreOneDistrict())
                        {
                            m_NextAllowedDispatchSecond = nowSeconds + COOLDOWN_SECONDS;
                            actualSheddedCount = math.clamp(
                                m_StateReader.GetAutoSheddedCount(),
                                0,
                                AutoDispatchData.MAX_SHED_COUNT);
                            dispatchData.AutoSheddedCount = actualSheddedCount;
                            m_StabilitySeconds = 0f;
                            Log.Info($"[AutoDispatch] RECOVERY: Restored district after {STABILITY_SECONDS_REQUIRED:F0}s stability. Remaining: {dispatchData.AutoSheddedCount}");
                        }
                        else
                        {
                            m_StabilitySeconds = 0f;
                        }
                    }
                }
                else if (hasPowerData)
                {
                    // Real deficit — reset stability. Skip when !hasPowerData (empty buffer
                    // window after load) to preserve persisted stability timer.
                    m_StabilitySeconds = 0f;
                }
            }

            ObserveAutoDispatchOwnedVersion(dispatchData);
            SystemAPI.SetComponent(dispatchEntity, dispatchData);
        }

        private void ObserveAutoDispatchOwnedVersion(in AutoDispatchData data)
        {
            BumpAutoDispatchRevisionIfRotated();
            m_AutoDispatchOwnershipView.Publish(new AutoDispatchOwnershipSnapshot(
                data.Enabled,
                data.AutoSheddedCount,
                data.IsBlockedByVip,
                m_AutoDispatchRevision));
        }

        // Pull the current auto-shedded set from the district-state service,
        // SetEquals against last tick's snapshot. Diff means identity rotation
        // happened (add, remove, or A↔B swap with stable cardinality) — bump
        // revision and mirror cur into prev. m_StateReader may be unresolved
        // during very early ticks; in that case we leave the previous
        // revision untouched.
        private void BumpAutoDispatchRevisionIfRotated()
        {
            if (m_StateReader == null)
                return;
            m_CurAutoShedded.Clear();
            foreach (var idx in m_StateReader.GetAutoSheddedDistricts())
                m_CurAutoShedded.Add(idx);
            if (m_CurAutoShedded.SetEquals(m_PrevAutoShedded))
                return;
#pragma warning disable CIVIC226 // Monotonic version stamp — overflow wraps, equality-only consumer.
            unchecked { m_AutoDispatchRevision++; }
#pragma warning restore CIVIC226
            m_PrevAutoShedded.Clear();
            m_PrevAutoShedded.UnionWith(m_CurAutoShedded);
        }

        // Shed load to cover targetMW (= deficit gap × SHED_MARGIN), straight to OFF, multiple units per
        // tick. Hybrid candidate pool: a whole real district, or one category of the Unzoned aggregate
        // (the undistricted city — most players never make districts, so this is the common/only source).
        private ShedResult ShedToTargetMW(int targetMW, bool isCritical)
        {
            // TOCTOU FIX: Check service and use TryGetSingletonEntity
            if (!EnsureServices())
                return ShedResult.NoAction;

            if (!m_DistrictPowerQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity))
                return ShedResult.NoAction;
            if (!m_DistrictPowerEntryLookup.TryGetBuffer(singletonEntity, out var powerBuffer)) return ShedResult.NoAction;

            // GC-FIX: Reuse list
            m_ShedCandidates.Clear();
            int vipCount = 0;

            for (int i = 0; i < powerBuffer.Length; i++)
            {
                var entry = powerBuffer[i];
                int idx = entry.District.Index;
                var data = entry.Data;

                if (entry.District.IsNull)
                {
                    // Unzoned / No-District aggregate → shed per category, keyed under the logical id the
                    // blackout/snapshot layer uses (CalculateActiveConsumption maps IsNull → NO_DISTRICT_INDEX),
                    // so the shed actually drops active consumption. Whole-block would nuke the whole city.
                    int uidx = Engine.Districts.NO_DISTRICT_INDEX;
                    if (m_StateReader.IsScheduleBlackoutActive(uidx)) continue;
                    foreach (var cat in BuildingCategories.All)
                    {
                        if (m_StateReader.IsCategoryOff(uidx, cat)) continue;
                        int catMW = CategoryMW(data, cat);
                        if (catMW <= 0) continue;
                        int catPrio = CategoryPriority.TryGetValue(cat, out int cp) ? cp : DEFAULT_CATEGORY_PRIORITY;
                        m_ShedCandidates.Add((uidx, cat, false, catPrio, catMW, false));
                    }
                    continue;
                }

                // WARN-4-3 fix: bounds check on district indices
                if (idx <= 0) continue;

                bool isVip = m_StateReader.IsVIP(idx);
                // Skip VIP in PANIC (count them); CRITICAL overrides VIP (physics > politics).
                if (isVip && !isCritical)
                {
                    vipCount++;
                    continue;
                }
                if (IsFullBlackout(idx)) continue;
                if (m_StateReader.IsScheduleBlackoutActive(idx)) continue;

                var dominantCat = GetDominantCategory(data);
                int priority = CategoryPriority.TryGetValue(dominantCat, out int p) ? p : DEFAULT_CATEGORY_PRIORITY;
                if (isVip) priority += 100; // VIP shed last, even in CRITICAL
                m_ShedCandidates.Add((idx, dominantCat, true, priority, data.TotalMW, isVip));
            }

            // BUG-INV-001 FIX: all candidates were VIP (PANIC mode) — nothing shed-able.
            if (m_ShedCandidates.Count == 0)
                return vipCount > 0 ? ShedResult.BlockedByVip : ShedResult.NoAction;

            // Sort: priority ASC (shed cheapest first), then MW DESC (biggest impact), then index.
            m_ShedCandidates.Sort(CompareShedUnits);

            int accMW = 0;
            int shedCount = 0;
            bool vipOverride = false;

            for (int i = 0; i < m_ShedCandidates.Count; i++)
            {
                if (accMW >= targetMW) break;
                var c = m_ShedCandidates[i];
                if (c.Whole)
                    ShedWholeDistrict(c.Index, c.IsVip, isCritical, c.Priority, c.MW, ref vipOverride);
                else
                    ShedUnzonedCategory(c.Index, c.Cat, c.MW);
                accMW += c.MW;
                shedCount++;
            }

            if (shedCount > 0)
                Log.Info($"[AutoDispatch] PANIC: shed {shedCount} unit(s), {accMW}/{targetMW} MW");

            if (shedCount == 0)
                return ShedResult.NoAction;
            return vipOverride ? ShedResult.SuccessVipOverride : ShedResult.Success;
        }

        // Shed a whole real district straight to OFF (blunt — crude vs a human who'd shed selectively).
        private void ShedWholeDistrict(int idx, bool isVip, bool isCritical, int priority, int mw, ref bool vipOverride)
        {
            if (!EnsureServices()) return;

            // Capture pre-shed state on FIRST shed so recovery can restore player intent.
            if (!m_StateReader.IsAutoShedded(idx))
            {
                var preShed = PreShedState.Capture(
                    m_StateReader.GetSchedule(idx),
                    BuildingCategories.All,
                    cat => m_StateReader.IsCategoryOff(idx, cat),
                    isVip: isVip,
                    isVipBypass: m_StateReader.IsVIPBypass(idx),
                    hadExplicitSchedule: m_StateReader.HasCustomSchedule(idx));
                m_AutoDispatchWriter.SetAutoShedded(idx, preShed);
            }

            if (isVip && isCritical)
            {
                if (m_StateReader.IsVIPBypass(idx))
                    m_AutoDispatchWriter.ToggleVIPBypassAutoDispatch(idx);
                m_AutoDispatchWriter.ToggleVIPAutoDispatch(idx);
                vipOverride = true;
                string districtName = m_StateReader.GetDistrictName(idx);
                EventBus?.SafePublish(new CorruptionNarrativeEvent(
                    CorruptionNarrativeEventType.VIPOverridden,
                    Location: districtName));
                Log.Warn($"[AutoDispatch] VIP OVERRIDE: District {idx} ({districtName}) lost VIP status and was force-shedded!");
            }

            // Straight to full blackout — Q1 is useless for relief (cyclic, only cuts in OFF-hours).
            foreach (var cat in BuildingCategories.All)
            {
                if (!m_StateReader.IsCategoryOff(idx, cat))
                    m_AutoDispatchWriter.ToggleDistrictCategoryAutoDispatch(idx, cat);
            }
            EventBus?.SafePublish(new DistrictStateChangedEvent(idx));
            Log.Info($"[AutoDispatch] District {idx} → OFF (prio:{priority}, MW:{mw})");
        }

        // Shed one category of the Unzoned aggregate (the whole undistricted city) — proportional.
        private void ShedUnzonedCategory(int uidx, BuildingCategory cat, int mw)
        {
            if (!EnsureServices()) return;

            if (!m_StateReader.IsAutoShedded(uidx))
            {
                var preShed = PreShedState.Capture(
                    m_StateReader.GetSchedule(uidx),
                    BuildingCategories.All,
                    c => m_StateReader.IsCategoryOff(uidx, c),
                    isVip: false,
                    isVipBypass: false,
                    hadExplicitSchedule: m_StateReader.HasCustomSchedule(uidx));
                m_AutoDispatchWriter.SetAutoShedded(uidx, preShed);
            }

            m_AutoDispatchWriter.ToggleDistrictCategoryAutoDispatch(uidx, cat);
            EventBus?.SafePublish(new DistrictStateChangedEvent(uidx));
            Log.Info($"[AutoDispatch] Unzoned {cat} → OFF (MW:{mw})");
        }

        private static int CategoryMW(in DistrictPowerData data, BuildingCategory cat) => cat switch
        {
            BuildingCategory.Services => data.ServicesMW,
            BuildingCategory.Office => data.OfficeMW,
            BuildingCategory.Commercial => data.CommercialMW,
            BuildingCategory.Residential => data.ResidentialMW,
            BuildingCategory.Industrial => data.IndustrialMW,
            BuildingCategory.None => 0,
            _ => throw new System.ArgumentOutOfRangeException(nameof(cat), cat, "Unknown building category")
        };

        // Shed order: priority ASC (cheapest first), then MW DESC (biggest impact), then index.
        private static int CompareShedUnits(
            (int Index, BuildingCategory Cat, bool Whole, int Priority, int MW, bool IsVip) a,
            (int Index, BuildingCategory Cat, bool Whole, int Priority, int MW, bool IsVip) b)
        {
            if (a.Priority != b.Priority) return a.Priority.CompareTo(b.Priority);
            if (a.MW != b.MW) return b.MW.CompareTo(a.MW);
            return a.Index.CompareTo(b.Index);
        }

        private bool TryRestoreOneDistrict()
        {
            // TOCTOU FIX: Check service and use TryGetSingletonEntity
            if (!EnsureServices())
                return false;

            if (!m_DistrictPowerQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity))
                return false;
            if (!m_DistrictPowerEntryLookup.TryGetBuffer(singletonEntity, out var powerBuffer)) return false;

            var autoShedded = m_StateReader.GetAutoSheddedDistricts();

            // GC-FIX: Reuse collections
            m_RestoreCandidates.Clear();
            m_PowerLookup.Clear();

            // Build lookup
            for (int i = 0; i < powerBuffer.Length; i++)
            {
                var entry = powerBuffer[i];
                m_PowerLookup[entry.District.Index] = entry.Data;
            }

            foreach (int idx in autoShedded)
            {
                if (!m_PowerLookup.TryGetValue(idx, out var data))
                {
                    // District demolished — clear orphaned flag
                    m_AutoDispatchWriter.ClearAutoShedded(idx);
                    Log.Info($"[AutoDispatch] District {idx}: orphaned AutoShedded cleared");
                    continue;
                }

                var dominantCat = GetDominantCategory(data);
                int priority = CategoryPriority.TryGetValue(dominantCat, out int p) ? p : DEFAULT_CATEGORY_PRIORITY;

                m_RestoreCandidates.Add((idx, priority, data.TotalMW));
            }

            if (m_RestoreCandidates.Count == 0) return false;

            // AUDIT FIX H2.3: Manual max-finding instead of LINQ OrderBy chain (avoids allocation)
            // Restore in REVERSE priority order (Industrial first - most important)
            // And smallest MW first (safer restoration)
            // Sort criteria: priority DESC (restore last-shed first), then MW ASC (smallest first)
            var target = m_RestoreCandidates[0];
            for (int i = 1; i < m_RestoreCandidates.Count; i++)
            {
                var c = m_RestoreCandidates[i];
                if (c.Priority > target.Priority ||
                    (c.Priority == target.Priority && c.MW < target.MW) ||
                    (c.Priority == target.Priority && c.MW == target.MW && c.Index < target.Index))
                {
                    target = c;
                }
            }

            // Check current state
            bool isOff = IsFullBlackout(target.Index);
            var schedule = m_StateReader.GetSchedule(target.Index);

            if (!m_StateReader.TryGetPreShedState(target.Index, out var preShed))
            {
                // No PreShedState — stale flag, clear and skip
                m_AutoDispatchWriter.ClearAutoShedded(target.Index);
                Log.Info($"[AutoDispatch] District {target.Index}: no PreShedState, cleared");
                return false;
            }

            if (isOff)
            {
                // OFF → intermediate: restore only categories that AutoDispatch turned off
                // (preserve player's manual category overrides)
                foreach (var cat in BuildingCategories.All)
                {
                    bool isCurrentlyOff = m_StateReader.IsCategoryOff(target.Index, cat);
                    bool wasOffByPlayer = preShed.CategoriesOff.Contains(cat);
                    if (isCurrentlyOff && !wasOffByPlayer)
                        m_AutoDispatchWriter.ToggleDistrictCategoryAutoDispatch(target.Index, cat);
                }
                if (IsOnlyPlayerBlackoutRemaining(target.Index, preShed))
                {
                    RestoreVipIfNeeded(target.Index, preShed);
                    m_AutoDispatchWriter.ClearAutoShedded(target.Index);
                    EventBus?.SafePublish(new DistrictStateChangedEvent(target.Index));
                    Log.Info($"[AutoDispatch] District {target.Index}: auto-shed cleared; player blackout preserved");
                    return true;
                }
                // Intermediate step: Q1 for safety (don't jump to original schedule yet)
                m_AutoDispatchWriter.SetDistrictScheduleAutoDispatch(target.Index, SchedulePreset.MildRestriction);
                EventBus?.SafePublish(new DistrictStateChangedEvent(target.Index));
                Log.Info($"[AutoDispatch] District {target.Index} → Q1 (player cats preserved)");
            }
            else if (schedule != SchedulePreset.Manual)
            {
                // Q1 → original schedule (restore player intent, clear auto-shedded)
                if (preShed.HadExplicitSchedule)
                    m_AutoDispatchWriter.SetDistrictScheduleAutoDispatch(target.Index, preShed.Schedule);
                else
                    m_AutoDispatchWriter.ClearDistrictScheduleAutoDispatch(target.Index);

                RestoreVipIfNeeded(target.Index, preShed);

                m_AutoDispatchWriter.ClearAutoShedded(target.Index);
                EventBus?.SafePublish(new DistrictStateChangedEvent(target.Index));
                Log.Info($"[AutoDispatch] District {target.Index} → {preShed.Schedule} (restored to player intent)");
            }
            else
            {
                // Stale AutoShedded flag — player already restored manually
                m_AutoDispatchWriter.ClearAutoShedded(target.Index);
                Log.Info($"[AutoDispatch] District {target.Index}: stale AutoShedded cleared");
                return false;
            }

            return true;
        }

        private bool IsOnlyPlayerBlackoutRemaining(int districtIndex, in PreShedState preShed)
        {
            if (!EnsureServices()) return false;
            foreach (var cat in BuildingCategories.All)
            {
                if (m_StateReader.IsCategoryOff(districtIndex, cat) &&
                    !preShed.CategoriesOff.Contains(cat))
                {
                    return false;
                }
            }
            return true;
        }

        private void RestoreVipIfNeeded(int districtIndex, in PreShedState preShed)
        {
            if (!EnsureServices()) return;

            bool changed = false;
            if (m_StateReader.IsVIP(districtIndex) != preShed.WasVip)
            {
                m_AutoDispatchWriter.ToggleVIPAutoDispatch(districtIndex);
                changed = true;
            }

            if (m_StateReader.IsVIPBypass(districtIndex) != preShed.WasVipBypass)
            {
                m_AutoDispatchWriter.ToggleVIPBypassAutoDispatch(districtIndex);
                changed = true;
            }

            if (changed)
                Log.Info($"[AutoDispatch] District {districtIndex}: VIP intent restored (vip={preShed.WasVip}, bypass={preShed.WasVipBypass})");
        }

        private bool IsFullBlackout(int districtIndex)
        {
            if (!EnsureServices()) return false;
            foreach (var cat in BuildingCategories.All)
            {
                if (!m_StateReader.IsCategoryOff(districtIndex, cat))
                    return false;
            }
            return true;
        }

        [MemberNotNullWhen(true, nameof(m_StateReader), nameof(m_StateWriter), nameof(m_AutoDispatchWriter))]
        private bool EnsureServices()
        {
            m_StateReader ??= ServiceRegistry.TryGet<IDistrictStateReader>();
            m_StateWriter ??= ServiceRegistry.TryGet<IDistrictStateWriter>();
            m_AutoDispatchWriter ??= ServiceRegistry.TryGet<IAutoDispatchStateWriter>();

            if (m_StateReader != null && m_StateWriter != null && m_AutoDispatchWriter != null)
                return true;

            if (!m_ServiceWarningLogged)
            {
                Log.Warn("[AutoDispatch] Required services not available - system disabled");
                m_ServiceWarningLogged = true;
            }
            return false;
        }

#pragma warning disable CIVIC006 // Singleton ensure: re-create after load without overwriting existing state.
        private void EnsureAutoDispatchSingleton()
        {
            if (!m_AutoDispatchQuery.IsEmptyIgnoreFilter)
                return;

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, AutoDispatchData.CreateDefault());
            EntityManager.SetName(entity, "AutoDispatchData");
            Log.Info("AutoDispatchSystem: Created AutoDispatchData singleton");
        }
#pragma warning restore CIVIC006

        private BuildingCategory GetDominantCategory(DistrictPowerData data)
        {
            int maxMW = data.OfficeMW;
            var dominant = BuildingCategory.Office;

            if (data.CommercialMW > maxMW)
            {
                maxMW = data.CommercialMW;
                dominant = BuildingCategory.Commercial;
            }
            if (data.ResidentialMW > maxMW)
            {
                maxMW = data.ResidentialMW;
                dominant = BuildingCategory.Residential;
            }
            if (data.IndustrialMW > maxMW)
            {
                maxMW = data.IndustrialMW;
                dominant = BuildingCategory.Industrial;
            }
            if (data.ServicesMW > maxMW)
            {
#pragma warning disable S1854 // Defensive: keeps maxMW consistent if branches are added after Services
                maxMW = data.ServicesMW;
#pragma warning restore S1854
                dominant = BuildingCategory.Services;
            }

            return dominant;
        }
    }
}
