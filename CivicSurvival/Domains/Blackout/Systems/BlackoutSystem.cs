using Game;
using Game.Common;
using CivicSurvival.Core.Features.Wellbeing;
using Game.Simulation;
using Game.Buildings;
using Game.Tools;
using Game.Areas;
using Game.Prefabs;
using Game.Citizens;
using Game.Economy;
using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using System;
using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Blackout.Data;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Blackout.Systems
{
    /// <summary>
    /// High-performance blackout system using IJobEntity + Burst + ScheduleParallel.
    /// Uses IEnableableComponent for zero-cost enable/disable without structural changes.
    /// Excludes critical infrastructure at query level (no per-entity checks needed).
    ///
    /// Thread-safe: Uses ThreadSafeDistrictState for all state management.
    /// Takes immutable snapshot at frame start for safe job scheduling.
    /// </summary>
    [ActIndependent]
#pragma warning disable CIVIC321 // m_BufferSlotHandles completed in OnDestroy/ResetState/Deserialize, not OnBecameDisabled
    public partial class BlackoutSystem : ThrottledSystemBase, IDefaultSerializable, IResettable, IBlackoutStateVersionReader
#pragma warning restore CIVIC321
    {
        private static readonly LogContext Log = new("BlackoutSystem");

        // Pre-allocated penalty source arrays to avoid GC allocation per tick
        private static readonly PenaltySource[] s_InactiveForScheduled = { PenaltySource.Blackout, PenaltySource.AutoDispatch };
        private static readonly PenaltySource[] s_InactiveForAuto = { PenaltySource.Blackout, PenaltySource.ScheduledBlackout };
        private static readonly PenaltySource[] s_InactiveForManual = { PenaltySource.ScheduledBlackout, PenaltySource.AutoDispatch };

        // Thread-safe state container (cached to avoid ServiceRegistry.Get per call)
#pragma warning disable CIVIC031, S2696 // Cleared in OnCreate/OnDestroy — static for thread-safe job access
        private static IDistrictStateReader? s_CachedReader;
        private static IDistrictStateWriter? s_CachedWriter;
        private static IDistrictStateSerialization? s_CachedSerialization;
#pragma warning restore CIVIC031, S2696
#pragma warning disable CIVIC165 // Cached behind null check — only resolves once
        private static IDistrictStateReader? StateReader
        {
            get
            {
                if (s_CachedReader != null) return s_CachedReader;
                if (!ServiceRegistry.IsInitialized) return null;
                s_CachedReader = ServiceRegistry.Instance.Require<IDistrictStateReader>();
                return s_CachedReader;
            }
        }

        // FIX S08_CODE2:96: Cache-once pattern (same as StateReader) — avoids repeated ServiceRegistry.TryGet
        private static IDistrictStateWriter? StateWriter
        {
            get
            {
                if (s_CachedWriter != null) return s_CachedWriter;
                if (!ServiceRegistry.IsInitialized) return null;
                s_CachedWriter = ServiceRegistry.Instance.Require<IDistrictStateWriter>();
                return s_CachedWriter;
            }
        }

        private static IDistrictStateSerialization? StateSerialization
        {
            get
            {
                if (s_CachedSerialization != null) return s_CachedSerialization;
                if (!ServiceRegistry.IsInitialized) return null;
                s_CachedSerialization = ServiceRegistry.Instance.Require<IDistrictStateSerialization>();
                return s_CachedSerialization;
            }
        }
#pragma warning restore CIVIC165

        // Query for buildings that have BlackoutState component (setup by BlackoutStateSetupSystem)
        private EntityQuery m_BlackoutableQuery;

        // Native data for jobs — re-populated every frame, not persisted
        // Triple-buffer: each frame, main thread writes fresh data to m_Buffers[m_WriteIndex].
        // Job is created from this buffer, then m_WriteIndex swaps (1 - m_WriteIndex).
        // Triple-buffered: write to m_Buffers[m_WriteIndex], job reads same buffer, advance index.
        // With 3 buffers, N-2 job normally completes within 2 fire cycles — per-slot handles
        // guarantee safety even under worst-case scheduling. Complete() is a noop in normal case.
#pragma warning disable CIVIC314 // Infrastructure containers — disposed in OnDestroy
        [System.NonSerialized] private BlackoutData[] m_Buffers = null!;
#pragma warning restore CIVIC314
        private Unity.Jobs.JobHandle[] m_BufferSlotHandles = null!;
        [System.NonSerialized] private int m_WriteIndex; // 0, 1, or 2 — index of buffer main thread writes to

        // Component lookups (cached for job)
        private ComponentLookup<ResidentialProperty> m_ResidentialLookup;
        private ComponentLookup<CommercialProperty> m_CommercialLookup;
        private ComponentLookup<IndustrialProperty> m_IndustrialLookup;
        private ComponentLookup<OfficeProperty> m_OfficeLookup;
        private ComponentLookup<BackupPower> m_BackupPowerLookup;

        // Building → live backup mod-entity link map (replaces the BackupPowerRef component lookup).
        private IBackupPowerLinkReader? m_LinkReader;
        private IBackupPowerLinkReader? LinkReader
        {
            get
            {
                if (m_LinkReader != null) return m_LinkReader;
                if (!ServiceRegistry.IsInitialized) return null;
                m_LinkReader = ServiceRegistry.Instance.Require<IBackupPowerLinkReader>();
                return m_LinkReader;
            }
        }

        // VIP Bypass: Wealthy household lookups
        private BufferLookup<Renter> m_RenterLookup;
        private ComponentLookup<Household> m_HouseholdLookup;
        private BufferLookup<Game.Economy.Resources> m_ResourcesLookup;

        // Penalty buffer lookup (replaces EntityManager.GetBuffer sync point)
        private BufferLookup<PenaltyRequest> m_PenaltyRequestLookup;

        // Cached settings (SMELL-01 fix: avoid ServiceRegistry.Get in hot path)
        private ModSettings? m_Settings;
        private ModSettings? Settings
        {
            get
            {
                if (m_Settings != null) return m_Settings;
                if (!ServiceRegistry.IsInitialized) return null;
                m_Settings = ServiceRegistry.Instance.Require<ModSettings>();
                return m_Settings;
            }
        }

        // Query for PenaltyRequest buffer (Data-Driven Commands)
        private EntityQuery m_PenaltyRequestQuery;
        private EntityQuery m_LiveDistrictQuery;
        private EntityQuery m_BackupPowerStateQuery;
        private EntityQuery m_ShadowExportQuery;

        // Cached live district indices — refreshed only when District topology changes
        // (vanilla creates/deletes a District). Avoids ToEntityArray sync per penalty tick.
        [System.NonSerialized] private NativeList<int> m_LiveDistrictIndicesCache;
        [EntityQueryOrderCursor("Invalidates the live-district index cache when the district query's archetype set changes.")]
        [System.NonSerialized] private uint m_LastDistrictOrderVersion;

        // Reusable scratch set for UpdateBlackoutPenalties (avoids GC allocation per frame)
        [NonEntityIndex] private readonly HashSet<int> m_PenaltyDistrictScratch = new();

        // Throttle penalty buffer updates to match DistrictPenaltySystem (UPDATE_INTERVAL_500_MS / UpdateInterval).
        // Without this, the buffer accumulates redundant requests per DistrictPenaltySystem cycle.
        [System.NonSerialized] private int m_PenaltyUpdateCounter;
        private const int PENALTY_UPDATE_STRIDE = Engine.Timing.UPDATE_INTERVAL_500_MS / 2; // divisor = UpdateInterval(2)

        // H14: Track previous HasAnyState to detect true→false transition.
        // When state disappears, one final job pass clears stuck BlackoutState on buildings.
        [System.NonSerialized] private bool m_HadAnyState;
        [System.NonSerialized] private bool m_PendingFinalClearPass;
        [System.NonSerialized] private readonly VersionedView<int> m_BlackoutStateView = new(0);
#pragma warning disable CIVIC324 // Ephemeral observed dirty marker; ResetState forces consumers to re-sync.
        [System.NonSerialized] private readonly VersionedView<int> m_ObservedDistrictStateVersion = new(0);
        [System.NonSerialized] private int m_DistrictStateObserverCursor = int.MinValue;
#pragma warning restore CIVIC324
        public IVersionedView<int> BlackoutStateView => m_BlackoutStateView;

        // Current frame snapshot (immutable, safe to iterate) — re-taken every frame
        [System.NonSerialized] private DistrictStateSnapshot m_CurrentSnapshotReader;

        // Use BuildingCategories.All from Core.Types
        public static System.Collections.Generic.IReadOnlyList<BuildingCategory> AllCategories => BuildingCategories.All;

        // PERF-LOCK: interval=2, do NOT raise — BlackoutJob re-zeros consumer.m_FulfilledConsumption
        // every pass to override vanilla DispatchElectricitySystem (GetUpdateInterval=128), which keeps
        // restoring power to blackout buildings. A higher interval widens the false-power window
        // (~interval/128 of the time) → blackout buildings visibly flicker power/efficiency. Idle cost
        // is already zero: ShouldSkipUpdate sleeps the whole system when no district has blackout state.
        protected override int UpdateInterval => 2;

        /// <summary>
        /// Check if blackout should be active for a district based on its schedule.
        /// Thread-safe: delegates to ThreadSafeDistrictState.
        /// </summary>
        public static bool IsBlackoutActiveForSchedule(int districtIndex)
        {
            var s = StateReader;
            if (s == null) { return false; }
            return s.IsScheduleBlackoutActive(districtIndex);
        }

        protected override void OnCreate()
        {
            base.OnCreate();
#pragma warning disable S2696 // Intentional: clear stale static cache from previous world
            s_CachedReader = null;
            s_CachedWriter = null;
            s_CachedSerialization = null;
#pragma warning restore S2696
            Log.Info($"{nameof(BlackoutSystem)} created (IJobEntity + ScheduleParallel)");
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IBlackoutStateVersionReader>(this);

            // Query: Buildings that have BlackoutState component
            m_BlackoutableQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadWrite<ElectricityConsumer>(),
                    ComponentType.ReadOnly<CurrentDistrict>(),
                    ComponentType.ReadWrite<BlackoutState>()
                },
                None = new[] {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            // Allocate triple-buffered native containers
            m_Buffers = new[]
            {
                BlackoutData.Create(Engine.DataStructures.MEDIUM_CAPACITY),
                BlackoutData.Create(Engine.DataStructures.MEDIUM_CAPACITY),
                BlackoutData.Create(Engine.DataStructures.MEDIUM_CAPACITY)
            };
            m_WriteIndex = 0;
            m_BufferSlotHandles = new Unity.Jobs.JobHandle[3];

            // Cache lookups
            m_ResidentialLookup = GetComponentLookup<ResidentialProperty>(true);
            m_CommercialLookup = GetComponentLookup<CommercialProperty>(true);
            m_IndustrialLookup = GetComponentLookup<IndustrialProperty>(true);
            m_OfficeLookup = GetComponentLookup<OfficeProperty>(true);
            m_BackupPowerLookup = GetComponentLookup<BackupPower>(true);

            // VIP Bypass lookups
            m_RenterLookup = GetBufferLookup<Renter>(true);
            m_HouseholdLookup = GetComponentLookup<Household>(true);
            m_ResourcesLookup = GetBufferLookup<Game.Economy.Resources>(true);

            // Query for PenaltyRequest buffer singleton (Data-Driven Commands)
            m_PenaltyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<PenaltyRequestSingleton>());
            m_LiveDistrictQuery = GetEntityQuery(
                ComponentType.ReadOnly<District>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_BackupPowerStateQuery = GetEntityQuery(ComponentType.ReadOnly<BackupPowerStateSingleton>());
            m_ShadowExportQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowExportState>());
            m_LiveDistrictIndicesCache = new NativeList<int>(32, Allocator.Persistent);
            m_LastDistrictOrderVersion = 0u;

            // Penalty buffer lookup (avoids EntityManager.GetBuffer sync point)
            m_PenaltyRequestLookup = GetBufferLookup<PenaltyRequest>(false);

            // S08-L1: No Enabled=false — ShouldSkipUpdate() handles null gracefully.
            // Settings are resolved lazily because ServiceRegistry may come online after OnCreate.
        }

        protected override bool ShouldSkipUpdate()
        {
            if (Settings == null) return true;
            var state = StateReader;
            if (state == null) return true;

            bool hasState = state.HasAnyState;
            if (hasState)
            {
                m_HadAnyState = true;
                m_PendingFinalClearPass = false;
                return false;
            }

            // H14: When HasAnyState transitions true→false, run one final pass
            // so BlackoutJob clears stuck BlackoutState.Active on buildings.
            // The latch is consumed in OnThrottledUpdate, after the throttle gate.
            if (m_HadAnyState || m_PendingFinalClearPass)
            {
                m_PendingFinalClearPass = true;
                return false; // allow one final update with empty state → job clears all
            }

            return true;
        }

        protected override void OnThrottledUpdate()
        {
            // Take snapshot only when actually processing (after all guards)
            var state = StateReader;
            if (state == null) return;
            bool hasState = state.HasAnyState;
            m_CurrentSnapshotReader = state.TakeSnapshot();
            ObserveBlackoutStateVersion(state.BlackoutStateVersion);

            using (PerformanceProfiler.Measure("BlackoutSystem.OnUpdate"))
            {
                RunBlackoutLogic();
            }

            if (!hasState && m_PendingFinalClearPass)
            {
                m_PendingFinalClearPass = false;
                m_HadAnyState = false;
            }
        }

        private void ObserveBlackoutStateVersion(int stateVersion)
        {
            m_ObservedDistrictStateVersion.Publish(stateVersion);
            if (!m_ObservedDistrictStateVersion.Observe(ref m_DistrictStateObserverCursor).Changed)
                return;

            m_BlackoutStateView.Publish(stateVersion);
        }

        private void RunBlackoutLogic()
        {
            // Triple-buffer: N-2 job normally completed — Complete() is a noop.
            // Guarantees safety under worst-case scheduling delay.
            m_BufferSlotHandles[m_WriteIndex].Complete();

            bool hasBlackoutableBuildings = !m_BlackoutableQuery.IsEmpty;
            bool shouldUpdatePenalties = ++m_PenaltyUpdateCounter >= PENALTY_UPDATE_STRIDE;
            m_PenaltyRequestLookup.Update(this);

            if (!hasBlackoutableBuildings && !shouldUpdatePenalties)
                return;

            if (!hasBlackoutableBuildings)
            {
                m_PenaltyUpdateCounter = 0;
                UpdateBlackoutPenalties();
                return;
            }

            if (!SyncSnapshotToNative()) return;

            // Update lookups for this frame
            m_ResidentialLookup.Update(this);
            m_CommercialLookup.Update(this);
            m_IndustrialLookup.Update(this);
            m_OfficeLookup.Update(this);
            m_BackupPowerLookup.Update(this);

            // VIP Bypass lookups
            m_RenterLookup.Update(this);
            m_HouseholdLookup.Update(this);
            m_ResourcesLookup.Update(this);

            // Wealthy threshold from BalanceConfig
            int wealthyThreshold = Core.Config.BalanceConfig.Current.Corruption.WealthyThreshold;

            // T2-12 FIX: Read backup policy for policy-aware blackout exemption
            var backupPolicy = (m_BackupPowerStateQuery.TryGetSingleton<BackupPowerStateSingleton>(out var bp)
                    ? bp : BackupPowerStateSingleton.Default)
                .Policy;
            var settings = Settings;
            if (settings == null)
                return;

            // Update penalties for districts in blackout (visual indicators).
            // Throttled to match DistrictPenaltySystem consumption rate — avoids flooding buffer
            // with redundant requests on every 2-frame BlackoutSystem tick.
            if (shouldUpdatePenalties)
            {
                m_PenaltyUpdateCounter = 0;
                UpdateBlackoutPenalties();
            }

            // Backup link map snapshot (read slot). Combine its last-reader handle so the job waits
            // for the owner's wholesale rebuild; register this job so the owner completes it before
            // reusing that slot. Service comes online at startup — skip a frame if not yet ready.
            var linkReader = LinkReader;
            if (linkReader == null) return;
            var backupLinks = linkReader.AcquireReadSnapshot(out int linkSlot);
            Dependency = Unity.Jobs.JobHandle.CombineDependencies(Dependency, linkReader.SlotHandle(linkSlot));

            // Create job from WRITE buffer (just synced with fresh data).
            // After ScheduleParallel, swap — so next frame writes to the OTHER buffer
            // while this job reads the current one.
            ref var writeBuffer = ref m_Buffers[m_WriteIndex];
            var job = new BlackoutJob
            {
                CategoryBlackouts = writeBuffer.CategoryBlackouts,
                DistrictSchedules = writeBuffer.DistrictSchedules,
                VIPDistricts = writeBuffer.VIPDistricts,
                VIPBypass = writeBuffer.VIPBypass,
                GameHour = writeBuffer.GameHour,

                ResidentialLookup = m_ResidentialLookup,
                CommercialLookup = m_CommercialLookup,
                IndustrialLookup = m_IndustrialLookup,
                OfficeLookup = m_OfficeLookup,

                BackupPowerLinks = backupLinks,
                BackupPowerLookup = m_BackupPowerLookup,
                BackupPowerEnabled = settings.BackupPowerEnabled,
                Policy = backupPolicy,

                // VIP Bypass: Wealthy household lookups
                RenterLookup = m_RenterLookup,
                HouseholdLookup = m_HouseholdLookup,
                ResourcesLookup = m_ResourcesLookup,
                WealthyThreshold = wealthyThreshold,

                // City-wide schedule fallback (for districts without per-district overrides)
                CityScheduleId = (int)m_CurrentSnapshotReader.CitySchedule,

                // Critical infra protection (IsCritical flag set at setup time on BlackoutState)
                ProtectCriticalInfra = settings.ProtectCriticalInfraEnabled
            };

            // Schedule PARALLEL - maximum performance!
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BlackoutJob.ScheduleParallel queryEmpty={!hasBlackoutableBuildings} slot={m_WriteIndex} buffer={writeBuffer.IsCreated} categoryBlackouts={m_CurrentSnapshotReader.DistrictBlackouts.Count} schedules={m_CurrentSnapshotReader.DistrictSchedules.Count} vip={m_CurrentSnapshotReader.VIPDistricts.Count} vipBypass={m_CurrentSnapshotReader.VIPBypass.Count} citySchedule={(int)m_CurrentSnapshotReader.CitySchedule} backup={settings.BackupPowerEnabled} protectCritical={settings.ProtectCriticalInfraEnabled}");
            Dependency = job.ScheduleParallel(m_BlackoutableQuery, Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BlackoutJob.ScheduleParallel queryEmpty={!hasBlackoutableBuildings} slot={m_WriteIndex} buffer={writeBuffer.IsCreated} categoryBlackouts={m_CurrentSnapshotReader.DistrictBlackouts.Count} schedules={m_CurrentSnapshotReader.DistrictSchedules.Count} vip={m_CurrentSnapshotReader.VIPDistricts.Count} vipBypass={m_CurrentSnapshotReader.VIPBypass.Count}");
            m_BufferSlotHandles[m_WriteIndex] = Dependency;
            // Register this job as a reader of the backup link slot so the owner completes it
            // before reusing that slot for a rebuild.
            linkReader.RegisterReader(linkSlot, Dependency);
            // Advance: next frame writes to the next buffer while this job reads current one.
            m_WriteIndex = (m_WriteIndex + 1) % 3;
        }

        /// <summary>
        /// Update penalty indicators for districts in blackout.
        /// Visual feedback for player (commerce/happiness penalties in UI).
        /// Note: Real economic impact comes from vanilla efficiency system (see BlackoutJob).
        ///
        /// MIGRATION: Uses PenaltyRequest buffer (Data-Driven Commands pattern).
        /// </summary>
        [CompletesDependency("UpdateBlackoutPenalties: District topology cache refresh gated by GetCombinedComponentOrderVersion delta — ToEntityArray fires only when player creates/deletes a district (rare; cached otherwise)")]
        private void UpdateBlackoutPenalties()
        {
            // Get PenaltyRequest buffer via cached lookup (avoids EntityManager sync point)
            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var singletonEntity))
                return;
            if (!m_PenaltyRequestLookup.HasBuffer(singletonEntity)) return;

            var penaltyBuffer = m_PenaltyRequestLookup[singletonEntity];

            // T10-7 fix: Collect ALL known district indices, not just manual blackouts.
            // Without this, schedule-only and auto-shedded districts never receive penalty indicators.
            m_PenaltyDistrictScratch.Clear();
            var allDistricts = m_PenaltyDistrictScratch;
            foreach (var key in m_CurrentSnapshotReader.DistrictBlackouts.Keys)
                allDistricts.Add(key);
            foreach (var key in m_CurrentSnapshotReader.DistrictSchedules.Keys)
                allDistricts.Add(key);
            foreach (int d in m_CurrentSnapshotReader.AutoSheddedDistricts)
                allDistricts.Add(d);
            // Include districts with existing penalties (to clear stale ones on recovery)
            foreach (var key in m_CurrentSnapshotReader.DistrictPenalties.Keys)
                allDistricts.Add(key);
            if (m_CurrentSnapshotReader.CitySchedule != SchedulePreset.Manual)
            {
                // Refresh cache only when District topology actually changed (creation/deletion).
                // GetCombinedComponentOrderVersion is metadata read, not a sync point.
                uint currentVersion = (uint)m_LiveDistrictQuery.GetCombinedComponentOrderVersion(includeEntityType: true);
                if (currentVersion != m_LastDistrictOrderVersion)
                {
                    m_LastDistrictOrderVersion = currentVersion;
                    m_LiveDistrictIndicesCache.Clear();
                    var liveDistricts = m_LiveDistrictQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < liveDistricts.Length; i++)
                        m_LiveDistrictIndicesCache.Add(liveDistricts[i].Index);
                    if (liveDistricts.IsCreated) liveDistricts.Dispose();
                }
                for (int i = 0; i < m_LiveDistrictIndicesCache.Length; i++)
                    allDistricts.Add(m_LiveDistrictIndicesCache[i]);
                allDistricts.Add(DistrictUtils.UNZONED_AREA_INDEX);
            }

            // FIX SM-02: Check if any VIP district has power (for VIPVisible penalty)
            // T10-7: Use IsDistrictInBlackout (checks auto+manual+schedule, not just manual)
            bool anyVIPHasPower = false;
            foreach (int vipDistrict in m_CurrentSnapshotReader.VIPDistricts)
            {
                if (!m_CurrentSnapshotReader.IsDistrictInBlackout(vipDistrict))
                {
                    anyVIPHasPower = true;
                    break;
                }
            }

            // ShadowExport morale penalty: city is selling power abroad while districts sit dark
            // ("why do they SELL our power while WE're dark?"). Read once per pass.
            bool exportingDuringCrisis =
                m_ShadowExportQuery.TryGetSingleton<ShadowExportState>(out var se) && se.ExportedMW > 0;

            // Iterate ALL known districts and update penalties based on blackout state
            foreach (int districtIndex in allDistricts)
            {
                bool isBlackedOut = m_CurrentSnapshotReader.IsDistrictInBlackout(districtIndex);
                bool isVIP = m_CurrentSnapshotReader.IsVIP(districtIndex);

                if (isBlackedOut)
                {
                    // Determine penalty source from blackout type
                    string source = m_CurrentSnapshotReader.GetBlackoutSource(districtIndex);
#pragma warning disable CIVIC135 // GetBlackoutSource returns string — enum conversion at boundary
                    var penaltySource = source switch
                    {
                        "schedule" => PenaltySource.ScheduledBlackout,
                        "auto" => PenaltySource.AutoDispatch,
                        _ => PenaltySource.Blackout
                    };
#pragma warning restore CIVIC135

                    penaltyBuffer.Add(new PenaltyRequest
                    {
                        DistrictIndex = districtIndex,
                        Source = penaltySource,
                        IsRemoval = false
                    });

                    // Remove inactive blackout penalty types (schedule↔manual↔auto transition)
#pragma warning disable CIVIC019 // Discard arm = PenaltySource.Blackout (manual) — all 3 sources covered
                    PenaltySource[] inactiveSources = penaltySource switch
                    {
                        PenaltySource.ScheduledBlackout => s_InactiveForScheduled,
                        PenaltySource.AutoDispatch => s_InactiveForAuto,
                        _ => s_InactiveForManual
                    };
#pragma warning restore CIVIC019
                    foreach (var inactive in inactiveSources)
                    {
                        penaltyBuffer.Add(new PenaltyRequest
                        {
                            DistrictIndex = districtIndex,
                            Source = inactive,
                            IsRemoval = true
                        });
                    }

                    // FIX SM-02: VIPVisible penalty — "Why do THEY have electricity and WE don't?"
                    penaltyBuffer.Add(new PenaltyRequest
                    {
                        DistrictIndex = districtIndex,
                        Source = PenaltySource.VIPVisible,
                        IsRemoval = isVIP || !anyVIPHasPower
                    });

                    // ShadowExport penalty — "Why do they SELL our power while WE're dark?"
                    penaltyBuffer.Add(new PenaltyRequest
                    {
                        DistrictIndex = districtIndex,
                        Source = PenaltySource.ShadowExport,
                        IsRemoval = !exportingDuringCrisis
                    });
                }
                else if (m_CurrentSnapshotReader.IsAutoShedded(districtIndex))
                {
                    // Q1 auto-managed district temporarily has power — keep AutoDispatch penalty
                    // to prevent oscillation (remove/re-add every shed/restore cycle).
                    // Only remove non-auto penalty types.
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.Blackout, IsRemoval = true });
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.ScheduledBlackout, IsRemoval = true });

                    // ShadowExport penalty — selling power abroad while this district is shed.
                    penaltyBuffer.Add(new PenaltyRequest
                    {
                        DistrictIndex = districtIndex,
                        Source = PenaltySource.ShadowExport,
                        IsRemoval = !exportingDuringCrisis
                    });
                }
                else
                {
                    // District not in blackout — remove all penalty types
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.Blackout, IsRemoval = true });
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.ScheduledBlackout, IsRemoval = true });
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.AutoDispatch, IsRemoval = true });
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.VIPVisible, IsRemoval = true });
                    penaltyBuffer.Add(new PenaltyRequest { DistrictIndex = districtIndex, Source = PenaltySource.ShadowExport, IsRemoval = true });
                }
            }
        }

        /// <summary>
        /// Sync immutable snapshot to native containers for Jobs.
        /// Called on main thread after snapshot taken.
        /// </summary>
        private bool SyncSnapshotToNative()
        {
            ref var writeBuffer = ref m_Buffers[m_WriteIndex];
            if (!writeBuffer.IsCreated)
            {
                Log.Error("SyncSnapshotToNative: write buffer not created — skipping blackout job");
                return false;
            }

            writeBuffer.SyncFromManaged(
                m_CurrentSnapshotReader.DistrictBlackouts,
                m_CurrentSnapshotReader.DistrictSchedules,
                m_CurrentSnapshotReader.VIPDistricts,
                m_CurrentSnapshotReader.VIPBypass,
                m_CurrentSnapshotReader.GameHour);
            return true;
        }

        protected override void OnDestroy()
        {
            Log.Info($"{nameof(BlackoutSystem)} destroyed");
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IBlackoutStateVersionReader>(this);

#pragma warning disable S2696 // Intentional: clear static cache on world destruction
            s_CachedReader = null;
            s_CachedWriter = null;
            s_CachedSerialization = null;
#pragma warning restore S2696
            if (m_BufferSlotHandles != null)
                for (int i = 0; i < m_BufferSlotHandles.Length; i++)
                    m_BufferSlotHandles[i].Complete();
            Dependency.Complete();
            if (m_Buffers != null)
            {
                for (int i = 0; i < m_Buffers.Length; i++)
                    m_Buffers[i].Dispose();
            }
            if (m_LiveDistrictIndicesCache.IsCreated)
                m_LiveDistrictIndicesCache.Dispose();

            base.OnDestroy();
        }

    }
}
