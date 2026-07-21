using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Features.Wellbeing;
using Game.Areas;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Blackout.Systems
{
    /// <summary>
    /// Monitors district blackout state and publishes start/end events.
    /// Keeps polling logic inside Blackout domain instead of Narrative.
    ///
    /// FIX S7-1: Also tracks blackout duration per district and VIP visibility.
    /// Publishes LongBlackoutEvent when threshold exceeded (default 4h).
    /// Publishes VIPVisibleDuringBlackoutEvent when VIP has power while others dark.
    /// </summary>
    [ActIndependent]
    public partial class BlackoutEventProducerSystem : ThrottledSystemBase, IResettable
    {
        private static readonly LogContext Log = new("BlackoutEventProducerSystem");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        [NonEntityIndex] private readonly Dictionary<int, bool> m_PreviousBlackoutState = new();
        [NonEntityIndex] private readonly HashSet<int> m_DistrictScratch = new();
        [NonEntityIndex] private readonly HashSet<int> m_LiveDistrictIndices = new();
        private IDistrictStateReader? m_DistrictState;
        private EntityQuery m_DistrictQuery;

        // FIX S7-1: Track blackout start time (game hours) per district
        [NonEntityIndex] private readonly Dictionary<int, float> m_BlackoutStartHours = new();
        // FIX S7-1: Track which districts already fired LongBlackoutEvent (once per blackout)
        [NonEntityIndex] private readonly HashSet<int> m_LongBlackoutFired = new();
        // FIX S7-1: Track which VIP districts already fired VIPVisibleEvent (once per blackout cycle)
        [NonEntityIndex] private readonly HashSet<int> m_VIPVisibleFired = new();
        [System.NonSerialized] private bool m_SyncFirstTick;
        // M08 fix: debounce — only clear VIP fired state after sustained non-blackout period
        private float m_LastNonVipBlackoutHour;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Cached vanilla district query — used to discover ALL districts when CitySchedule active.
            // Replaces DistrictPenalties proxy which missed penalty-free districts (H12).
            m_DistrictQuery = GetEntityQuery(
                ComponentType.ReadOnly<District>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_DistrictState ??= ServiceRegistry.Instance.Require<IDistrictStateReader>();
        }

        [CompletesDependency("OnThrottledUpdate: vanilla District query is read-only, throttled 1 s; ToEntityArray sync amortised against the per-district state diff and is negligible on a 1 Hz cadence")]
        protected override void OnThrottledUpdate()
        {
            // LOAD-INVARIANT: throttled runtime can tick before GameTime activation after load.
            if (!GameTimeSystem.TryGetGameHours(out var currentHours))
                return;

            // Skip first frame after load when GameHours not yet hydrated (H13: poisons start hours)
            if (currentHours <= 0f)
                return;
            var snapshot = m_DistrictState!.TakeSnapshot();

            // Post-load sync: align m_PreviousBlackoutState with current snapshot without firing events.
            // Prevents spurious BlackoutEnded when BlackoutSystem.Deserialize failed but BEPS succeeded.
            if (m_SyncFirstTick)
            {
                m_SyncFirstTick = false;
                m_PreviousBlackoutState.Clear();
                if (snapshot.DistrictBlackouts != null)
                {
                    foreach (var kvp in snapshot.DistrictBlackouts)
                        SeedPostLoadBlackoutState(snapshot, kvp.Key, currentHours);
                }
                if (snapshot.DistrictSchedules != null)
                {
                    foreach (var kvp in snapshot.DistrictSchedules)
                    {
                        if (!m_PreviousBlackoutState.ContainsKey(kvp.Key))
                            SeedPostLoadBlackoutState(snapshot, kvp.Key, currentHours);
                    }
                }
                // Seed city-wide schedule districts (same sources as normal processing path)
                if (snapshot.CitySchedule != SchedulePreset.Manual)
                {
                    var districts = m_DistrictQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < districts.Length; i++)
                    {
                        int idx = districts[i].Index;
                        if (!m_PreviousBlackoutState.ContainsKey(idx))
                            SeedPostLoadBlackoutState(snapshot, idx, currentHours);
                    }
                    if (districts.IsCreated) districts.Dispose();
                    if (!m_PreviousBlackoutState.ContainsKey(DistrictUtils.UNZONED_AREA_INDEX))
                        SeedPostLoadBlackoutState(snapshot, DistrictUtils.UNZONED_AREA_INDEX, currentHours);
                }
                // Seed auto-shed districts
                if (snapshot.AutoSheddedDistricts != null)
                {
                    foreach (int d in snapshot.AutoSheddedDistricts)
                    {
                        if (!m_PreviousBlackoutState.ContainsKey(d))
                            SeedPostLoadBlackoutState(snapshot, d, currentHours);
                    }
                }
                return;
            }

            m_DistrictScratch.Clear();

            // Always query live districts — needed both for city-wide schedule scratch
            // and for deleted-district validation in the event loop below.
            var liveDistricts = m_DistrictQuery.ToEntityArray(Allocator.Temp);
            m_LiveDistrictIndices.Clear();
            for (int i = 0; i < liveDistricts.Length; i++)
                m_LiveDistrictIndices.Add(liveDistricts[i].Index);
            if (liveDistricts.IsCreated) liveDistricts.Dispose();
            m_LiveDistrictIndices.Add(DistrictUtils.UNZONED_AREA_INDEX); // virtual — no entity

            if (snapshot.DistrictSchedules != null)
            {
                foreach (var kvp in snapshot.DistrictSchedules)
                    m_DistrictScratch.Add(kvp.Key);
            }

            if (snapshot.DistrictBlackouts != null)
            {
                foreach (var kvp in snapshot.DistrictBlackouts)
                    m_DistrictScratch.Add(kvp.Key);
            }

            // CitySchedule applies to ALL districts without custom override —
            // query vanilla District entities so events fire for penalty-free districts too (H12 fix).
            if (snapshot.CitySchedule != SchedulePreset.Manual)
            {
                foreach (int idx in m_LiveDistrictIndices)
                    m_DistrictScratch.Add(idx);
            }

            foreach (var kvp in m_PreviousBlackoutState)
                m_DistrictScratch.Add(kvp.Key);

            // Auto-shed districts may leave DistrictBlackouts during Q1 restore —
            // must be in scratch so their blackout state is tracked correctly.
            if (snapshot.AutoSheddedDistricts != null)
            {
                foreach (int d in snapshot.AutoSheddedDistricts)
                    m_DistrictScratch.Add(d);
            }

            bool anyNonVipBlackedOut = false;

            foreach (var districtId in m_DistrictScratch)
            {
                if (!m_LiveDistrictIndices.Contains(districtId))
                {
                    m_PreviousBlackoutState.Remove(districtId);
                    m_BlackoutStartHours.Remove(districtId);
                    m_LongBlackoutFired.Remove(districtId);
                    continue;
                }

                bool isBlackoutNow = snapshot.IsDistrictInBlackout(districtId);
                if (!m_PreviousBlackoutState.TryGetValue(districtId, out bool wasBlackout))
                    wasBlackout = false;
                bool isSuppressedAutoShedRecovery = !isBlackoutNow && wasBlackout && snapshot.IsAutoShedded(districtId);
                bool isLogicalBlackoutNow = isBlackoutNow || isSuppressedAutoShedRecovery;

                if (isBlackoutNow && !wasBlackout)
                {
                    EventBus?.SafePublish(new BlackoutStartedEvent(districtId), "BlackoutEventProducerSystem");
                    // FIX S7-05: Notify NeighborEnvy of schedule-based transitions
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtId), "BlackoutEventProducerSystem");
                    // FIX S7-1: Record blackout start time for duration tracking
                    m_BlackoutStartHours[districtId] = currentHours;
                    m_LongBlackoutFired.Remove(districtId);
                    Log.Info($"started: district {districtId}");
                }
                else if (!isBlackoutNow && wasBlackout && !snapshot.IsAutoShedded(districtId))
                {
                    EventBus?.SafePublish(new BlackoutEndedEvent(districtId), "BlackoutEventProducerSystem");
                    // A2 FIX: L1→L1 recovery event (replaces L3→L1 BlackoutRecoveryEvent from BlackoutNarrativeResolver)
                    EventBus?.SafePublish(new BlackoutRecoveredEvent(districtId), "BlackoutEventProducerSystem");
                    // FIX S7-05: Notify NeighborEnvy of schedule-based recovery
                    EventBus?.SafePublish(new DistrictStateChangedEvent(districtId), "BlackoutEventProducerSystem");
                    // FIX S7-1: Clean up duration tracking
                    m_BlackoutStartHours.Remove(districtId);
                    m_LongBlackoutFired.Remove(districtId);
                    Log.Info($"ended: district {districtId}");
                }

                // FIX S7-1: Check long blackout threshold
                if (isLogicalBlackoutNow
                    && !m_LongBlackoutFired.Contains(districtId)
                    && m_BlackoutStartHours.TryGetValue(districtId, out float startHours))
                {
                    float threshold = BalanceConfig.Current.Spotter.LongBlackoutThresholdHours;
                    float duration = currentHours - startHours;
                    if (duration < 0f)
                    {
                        m_BlackoutStartHours[districtId] = currentHours;
                        duration = 0f;
                    }
                    if (duration >= threshold)
                    {
                        EventBus?.SafePublish(new LongBlackoutEvent(districtId), "BlackoutEventProducerSystem");
                        m_LongBlackoutFired.Add(districtId);
                        if (Log.IsDebugEnabled) Log.Debug($"Long blackout ({threshold}h) reached: district {districtId}");
                    }
                }

                // Track whether any non-VIP district is in blackout (for VIP visibility check)
                if (isBlackoutNow && !snapshot.IsVIP(districtId))
                    anyNonVipBlackedOut = true;

                if (!isBlackoutNow
                    && (snapshot.DistrictBlackouts == null || !snapshot.DistrictBlackouts.ContainsKey(districtId))
                    && (snapshot.DistrictSchedules == null || !snapshot.DistrictSchedules.ContainsKey(districtId))
                    && !snapshot.IsAutoShedded(districtId))
                {
                    m_PreviousBlackoutState.Remove(districtId);
                    m_BlackoutStartHours.Remove(districtId);
                    m_LongBlackoutFired.Remove(districtId);
                }
                else
                {
                    m_PreviousBlackoutState[districtId] = isLogicalBlackoutNow;
                }
            }

            // FIX S7-1: Check VIP visibility — VIP has power while non-VIP districts are dark
            // M08 fix: debounce VIP visibility — only clear after 1h sustained non-blackout
            const float VIP_DEBOUNCE_HOURS = 1f;
            if (anyNonVipBlackedOut)
            {
                m_LastNonVipBlackoutHour = currentHours;
                foreach (int vipDistrict in snapshot.VIPDistricts)
                {
                    if (!snapshot.IsDistrictInBlackout(vipDistrict)
                        && !m_VIPVisibleFired.Contains(vipDistrict))
                    {
                        EventBus?.SafePublish(
                            new VIPVisibleDuringBlackoutEvent(vipDistrict),
                            "BlackoutEventProducerSystem");
                        m_VIPVisibleFired.Add(vipDistrict);
                        if (Log.IsDebugEnabled) Log.Debug($"VIP visible during blackout: VIP district {vipDistrict}");
                    }
                }
            }
            else if (m_VIPVisibleFired.Count > 0
                     && currentHours - m_LastNonVipBlackoutHour >= VIP_DEBOUNCE_HOURS)
            {
                // Sustained non-blackout period — reset for next blackout cycle
                m_VIPVisibleFired.Clear();
            }
        }

        private void SeedPostLoadBlackoutState(DistrictStateSnapshot snapshot, int districtId, float currentHours)
        {
            bool isBlackoutNow = snapshot.IsDistrictInBlackout(districtId);
            bool isSuppressedAutoShedRecovery = !isBlackoutNow && snapshot.IsAutoShedded(districtId);
            bool isLogicalBlackoutNow = isBlackoutNow || isSuppressedAutoShedRecovery;
            m_PreviousBlackoutState[districtId] = isLogicalBlackoutNow;

            if (isLogicalBlackoutNow)
            {
                if (!m_BlackoutStartHours.ContainsKey(districtId))
                    m_BlackoutStartHours[districtId] = currentHours;
            }
            else
            {
                m_BlackoutStartHours.Remove(districtId);
                m_LongBlackoutFired.Remove(districtId);
            }
        }

        /// <summary>
        /// FIX MED: Clear state on new game (IResettable).
        /// </summary>
        public void ResetState()
        {
            m_PreviousBlackoutState.Clear();
            m_DistrictScratch.Clear();
            m_LiveDistrictIndices.Clear();
            m_BlackoutStartHours.Clear();
            m_LongBlackoutFired.Clear();
            m_VIPVisibleFired.Clear();
            m_LastNonVipBlackoutHour = 0f;
            m_SyncFirstTick = false;
            Log.Debug("State reset for new game");
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_PreviousBlackoutState.Clear();
            m_DistrictScratch.Clear();
            m_LiveDistrictIndices.Clear();
            m_BlackoutStartHours.Clear();
            m_LongBlackoutFired.Clear();
            m_VIPVisibleFired.Clear();
            m_LastNonVipBlackoutHour = 0f;
            m_SyncFirstTick = true;
            if (Log.IsDebugEnabled)
                Log.Debug($"Boot-default reset after deserialize recovery: {reason}");
        }

        /// <summary>
        /// OD-008 FIX: Clear collections on destroy.
        /// </summary>
        protected override void OnDestroy()
        {
            m_PreviousBlackoutState.Clear();
            m_DistrictScratch.Clear();
            m_BlackoutStartHours.Clear();
            m_LongBlackoutFired.Clear();
            m_VIPVisibleFired.Clear();
            base.OnDestroy();
        }
    }
}
