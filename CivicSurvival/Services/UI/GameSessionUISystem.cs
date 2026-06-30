using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.CameraTracking;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Notifications.Services;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Services.UI
{
    /// <summary>
    /// Game-session half of the original MainUISystem. Owns bindings and triggers
    /// that read gameplay services (notifications, threat radar, camera tracking)
    /// and must only run after a city is loaded.
    ///
    /// Pair with <see cref="MainMenuShellUISystem"/> which owns the menu-safe half
    /// (UI theme, JS bridge, feature manifest).
    ///
    /// Registration ordering contract: SocialFeedService must be registered before
    /// this system's OnCreate runs, otherwise the social feed binding silently
    /// skips. NotificationsDomain registers the service in Mod.OnLoad, well before
    /// any UI system OnCreate, so the binding is always live here.
    /// </summary>
    [ActIndependent]
    public partial class GameSessionUISystem : CivicUISystemBase, IGameplayReadyListener
    {
        private static readonly LogContext Log = new("GameSessionUI");

        // Smart cycle: sorted by danger priority
        private int m_FocusThreatIndex;
        private EntityRef? m_LastFocusedThreatEntity;
        private List<RadarThreatDto> m_SortedThreats = null!;
        private bool m_InitialNotificationsSent;
        private bool m_SocialFeedBindingRegistered;

        protected override void OnUpdateImpl() { } // No per-frame logic — trigger/binding only

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Register the social feed (CHIPPER) binding directly on its owner,
            // SocialFeedService. Resolved here (not OnCreate) per CIVIC403 — services
            // are registered by Mod.OnLoad, but OnStartRunning is the sanctioned
            // resolution point. NotificationSystem no longer owns this binding; it
            // routes toasts only. Registration is one-shot (OnStartRunning may re-run
            // on enable/disable, but the binding must be added exactly once), so the
            // resolved instance is a local — it is never needed after registration.
            if (m_SocialFeedBindingRegistered)
                return;

            var socialFeed = ServiceRegistry.TryGet<SocialFeedService>();
            if (socialFeed != null)
            {
                socialFeed.RegisterBinding(AddUpdateBinding);
                m_SocialFeedBindingRegistered = true;
            }
            else
            {
                Log.Warn("SocialFeedService not registered — social feed binding skipped");
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Focus next threat trigger (cycles through all threats). Lifecycle wrap
            // suppresses stray menu-time invocations; existing NullThreatRadarReader
            // fallback would otherwise log "No active threats" and null-clear the
            // camera tracked entity.
            AddBinding(new TriggerBinding(Group, FocusNextThreat,
                CivicGameLifecycle.GameplayOnly(OnFocusNextThreat)));

            Log.Info(" Initialized (game-session bindings)");
        }

        // Auto-wired by CivicUISystemBase: fires from PLVS Phase 2 finally after city is
        // hydrated. Replaces the previous OnStartRunning hook, which fired on the first
        // eligible frame in cold-boot menu (no RequireForUpdate on UI systems) and pushed
        // startup toasts before any city existed.
        public void OnGameplayReady()
        {
            if (m_InitialNotificationsSent)
                return;

            SendInitialNotifications();
            m_InitialNotificationsSent = true;
        }

        /// <summary>
        /// Focus camera on next most-dangerous threat (smart cycle).
        /// Priority: Identified → Closest to city center → Lowest ETA.
        /// CameraTrackingSystem handles continuous following.
        /// </summary>
        private void OnFocusNextThreat()
        {
            // Resolve through the interface contract so a closed ThreatsAirDefense
            // feature degrades to an empty radar list instead of NRE.
            var radarReader = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatRadarReader.Instance);

            var threats = radarReader.GetRadarThreats();
            var cameraTracking = World.GetExistingSystemManaged<CameraTrackingSystem>();

            if (threats.Count == 0)
            {
                m_FocusThreatIndex = -1;
                m_LastFocusedThreatEntity = null;
                Log.Info(" No active threats to focus on");
                if (cameraTracking != null) cameraTracking.SetTrackedEntity(new EntityRef(0, 0));
                return;
            }

            // Build priority-sorted list: Identified → Closest to center → Lowest ETA
            if (m_SortedThreats == null) m_SortedThreats = new List<RadarThreatDto>(threats.Count);
            m_SortedThreats.Clear();
            for (int i = 0; i < threats.Count; i++) m_SortedThreats.Add(threats[i]);
            m_SortedThreats.Sort(CompareThreatPriority);

            var previousIndex = FindThreatIndex(m_LastFocusedThreatEntity, m_SortedThreats);
            m_FocusThreatIndex = previousIndex >= 0
                ? (previousIndex + 1) % m_SortedThreats.Count
                : 0;
            var threat = m_SortedThreats[m_FocusThreatIndex];
            m_LastFocusedThreatEntity = threat.Entity;

            if (cameraTracking != null) cameraTracking.SetTrackedEntity(threat.Entity);

            Log.Info($" Smart focus {m_FocusThreatIndex + 1}/{m_SortedThreats.Count} entity={threat.Entity.Index}v{threat.Entity.Version} identified={threat.IsIdentified} dist={DistToCenterSq(threat):F0} eta={threat.Eta:F0}s");
        }

        /// <summary>
        /// Compare threats by danger priority (for Sort — most dangerous first).
        /// 1. Identified before unidentified
        /// 2. Closer to city center (map origin) first
        /// 3. Lower ETA (arriving sooner) first
        /// </summary>
        private static int CompareThreatPriority(RadarThreatDto a, RadarThreatDto b)
        {
            // 1. Identified first
            if (a.IsIdentified != b.IsIdentified)
                return a.IsIdentified ? -1 : 1;

            // 2. Closest to city center (XZ distance² to map origin)
            float distA = DistToCenterSq(a);
            float distB = DistToCenterSq(b);
            int distCmp = distA.CompareTo(distB);
            if (distCmp != 0)
                return distCmp;

            // 3. Lowest ETA first (arriving sooner = more dangerous)
            return a.Eta.CompareTo(b.Eta);
        }

        private static float DistToCenterSq(RadarThreatDto t) => t.X * t.X + t.Z * t.Z;

        private static int FindThreatIndex(EntityRef? entity, List<RadarThreatDto> threats)
        {
            if (!entity.HasValue || threats == null) return -1;
            var value = entity.Value;
            for (int i = 0; i < threats.Count; i++)
            {
                if (threats[i].Entity.Index == value.Index && threats[i].Entity.Version == value.Version) return i;
            }

            return -1;
        }

        /// <summary>
        /// Send initial game notifications after UI bindings are registered.
        /// Includes Harmony patch failure warning so the player learns about
        /// broken mod compatibility on first city load (not in menu — patches
        /// may be critical for gameplay only).
        /// </summary>
        private void SendInitialNotifications()
        {
            var sink = ServiceRegistry.Instance.Require<NotificationState>();

            if (PatchStatusTracker.HasFailures)
            {
                var failedPatches = PatchStatusTracker.FailedPatches;
                var failedList = string.Join(", ", failedPatches);

                sink.Push(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    "patch_failed_warning",
                    "SYSTEM WARNING",
                    $"Mod compatibility issue: {failedPatches.Count} patches failed ({failedList}). Some features may not work. Check CivicSurvival.log for details.",
                    Status: NotificationStatus.Error
                ));
                Log.Warn($" Patch failure warning displayed to player: {failedList}");
            }

            // @CityAlert is an official-feed handle → Herald (content-stable NewsPostEvent).
            const string startupOfficialMsg = "Power grid monitoring system online. All districts operational. Maintain vigilance.";
            EventBus?.SafePublish(new NewsPostEvent(
                NotificationIdHelper.ContentId("@CityAlert", startupOfficialMsg, string.Empty, Engine.Narrative.NEWS_CONTENT_BUCKET_SECONDS),
                NewsAuthorRegistry.GetDisplayName("@CityAlert"),
                startupOfficialMsg,
                string.Empty,
                SocialMood.Neutral,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "official"), "GameSessionUISystem");

            // Citizen handles → CHIPPER (SocialPostEvent).
            EventBus?.SafePublish(new SocialPostEvent(
                "@BabcyaZina",
                "Grandson fixed the generator again. Third time this month. We'll manage.",
                SocialMood.Neutral), "GameSessionUISystem");

            EventBus?.SafePublish(new SocialPostEvent(
                "@InzhenerPetrenko",
                "Monitoring grid frequency. Everything stable... for now. Stay prepared, people.",
                SocialMood.Suspicious), "GameSessionUISystem");

            Log.Info(" Initial notifications sent");
        }
    }
}
