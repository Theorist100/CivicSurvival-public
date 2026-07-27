using System;
using System.Collections.Generic;
using Game;
using Game.Simulation;
using Game.UI.Menu;
using Game.UI.Localization;
using Unity.Entities;
using Colossal.Logging;
using CivicSurvival.Core.Utils;
using Colossal.PSI.Common;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Notifications.Systems
{
    /// <summary>
    /// Notification System — "Dumb Printer" for toasts only.
    ///
    /// Drains NotificationState and renders SystemAlert toasts via the vanilla
    /// NotificationUISystem. News (Herald) and social (CHIPPER) are addressed by the
    /// emitter directly through NewsPostEvent / SocialPostEvent — there is no author
    /// demux here: every DTO that reaches this system is a toast.
    ///
    /// Owns: NotificationState (registered in ServiceRegistry)
    /// Consumes notifications from state queue in OnUpdate.
    /// </summary>
    [ActIndependent]
    public partial class NotificationSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("NotificationSystem");

        // ============================================================================
        // STATE
        // ============================================================================

        // State (registered in ServiceRegistry)
        private NotificationState m_State = null!;

        private readonly object m_StateLock = new object();
        private Dictionary<string, double> m_LastNotificationTime = new Dictionary<string, double>();

        private const float PENDING_NOTIFICATION_DELAY = 8f;
        private const float COOLDOWN_CLEANUP_THRESHOLD = 300f;
        private const int CLEANUP_INTERVAL_FRAMES = Engine.Timing.UPDATE_INTERVAL_1_MINUTE;

        private int m_CleanupCounter;

        // GC-FIX H2.5: Cache list to avoid allocation in CleanupStaleCooldowns
        private readonly List<string> m_KeysToRemove = new List<string>();

        // Channel: Vanilla notifications
        private NotificationUISystem m_VanillaNotificationUI = null!;

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            m_VanillaNotificationUI = World.GetOrCreateSystemManaged<NotificationUISystem>();

            // Create and register state object (producers push to this)
            m_State = new NotificationState();
            // ProcessCallback / DrainAll removed — unsafe in serialization context (R4-S4-08)
            // Register unconditionally. ServiceRegistry.Initialize() runs in
            // Mod.OnLoad() before any system OnCreate, so `if (IsInitialized)` was a
            // dead guard whose only reachable effect was to silently skip
            // registration while consumers Require<NotificationState>()
            // unconditionally (registry-miss explodes inside Narrative). The very
            // next line already Require<>s unconditionally.
            ServiceRegistry.Instance.Register(m_State);

            Log.Info($"{nameof(NotificationSystem)} created (state registered)");
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            // FIX #164-A: ElapsedTime resets to ~0 on load, but cooldown timestamps
            // retain old values (e.g., 500.0). All CanNotify() checks would return false
            // until ElapsedTime catches up. Clear to restore notifications immediately.
            lock (m_StateLock)
            {
                m_LastNotificationTime.Clear();
            }
            m_CleanupCounter = 0;
            m_State.Clear();

            Log.Info("Cooldowns and pending notifications cleared on game load");
        }

        protected override void OnUpdateImpl()
        {
            // Process pending notifications from state queue
            while (m_State.TryDequeue(out var dto))
            {
                ProcessNotification(dto);
            }

            m_CleanupCounter++;
            if (m_CleanupCounter >= CLEANUP_INTERVAL_FRAMES)
            {
                m_CleanupCounter = 0;
                CleanupStaleCooldowns();
            }
        }

        private void ProcessNotification(NarrativeToastDto dto)
        {
            // PERF: Debug logging removed - stacktrace capture is expensive

            // Validate DTO fields
            if (string.IsNullOrEmpty(dto.Id))
            {
                Log.Warn("[NotificationSystem] Rejected: null/empty ID");
                return;
            }
            if (string.IsNullOrEmpty(dto.Title))
            {
                Log.Warn($"[NotificationSystem] Rejected: null/empty title for {dto.Id}");
                return;
            }
            if (string.IsNullOrEmpty(dto.Message))
            {
                Log.Warn($"[NotificationSystem] Rejected: null/empty message for {dto.Id}");
                return;
            }

            // Only toasts reach this system now — news/social are published directly as
            // NewsPostEvent / SocialPostEvent by the emitter, never via this sink.
            float cooldown = Core.Config.BalanceConfig.Current.Notifications.CooldownSystemAlert;

            if (!CanNotify(dto.Id, cooldown))
            {
                // PERF: Debug logging removed - stacktrace capture is expensive
                return;
            }

            if (PushSystemAlert(dto.Id, dto.Title, dto.Message, dto.Status))
                MarkNotified(dto.Id);
        }

        protected override void OnDestroy()
        {
            // OD-010 FIX: Clear dictionary on destroy
            lock (m_StateLock)
            {
                m_LastNotificationTime.Clear();
            }

            // Clear state queue
            m_State.Clear();

            // BUG-CORE-R07 FIX: Unregister service on destroy
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<NotificationState>(m_State);
            }
            Log.Info($"{nameof(NotificationSystem)} destroyed");
            base.OnDestroy();
        }

        // ============================================================================
        // CHANNELS
        // ============================================================================

        private bool PushSystemAlert(string id, string title, string message, NotificationStatus status)
        {
            // PERF: Info logging removed from hot-path

            try
            {
                // Map semantic status to vanilla ProgressState
                ProgressState state = status switch
                {
                    NotificationStatus.Info => ProgressState.Progressing,
                    NotificationStatus.Success => ProgressState.Complete,
                    NotificationStatus.Warning => ProgressState.Progressing,
                    NotificationStatus.Error => ProgressState.Failed,
                    _ => ProgressState.Progressing
                };

                m_VanillaNotificationUI.AddOrUpdateNotification(
                    identifier: $"civicsurvival_{id}",
                    title: LocalizedString.Value(title),
                    text: LocalizedString.Value(message),
                    thumbnail: null,
                    progressState: state,
                    progress: state == ProgressState.Complete ? 100 : 0,
                    onClicked: null
                );

                float delay = state == ProgressState.Complete ? 3f : PENDING_NOTIFICATION_DELAY;
                m_VanillaNotificationUI.RemoveNotification($"civicsurvival_{id}", delay: delay);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to push vanilla notification: {ex}");
                return false;
            }
        }

        // ============================================================================
        // COOLDOWN MANAGEMENT
        // ============================================================================

        private double GetCurrentTime()
        {
            // Vanilla notification removal delay is real-time; keep cooldowns on the
            // same clock so simulation speed cannot reopen an alert before its toast cycle.
            return UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        private bool CanNotify(string key, float cooldown)
        {
            double currentTime = GetCurrentTime();

            lock (m_StateLock)
            {
                if (!m_LastNotificationTime.TryGetValue(key, out double lastTime))
                    return true;
                return (currentTime - lastTime) >= cooldown;
            }
        }

        private void MarkNotified(string key)
        {
            double currentTime = GetCurrentTime();

            lock (m_StateLock)
            {
                m_LastNotificationTime[key] = currentTime;
            }
        }

        private void CleanupStaleCooldowns()
        {
            double currentTime = GetCurrentTime();

            lock (m_StateLock)
            {
                // GC-FIX H2.5: Reuse cached list instead of allocating new one
                m_KeysToRemove.Clear();
                foreach (var kvp in m_LastNotificationTime)
                {
                    if (currentTime - kvp.Value > COOLDOWN_CLEANUP_THRESHOLD)
                        m_KeysToRemove.Add(kvp.Key);
                }

                foreach (var key in m_KeysToRemove)
                    m_LastNotificationTime.Remove(key);

                // PERF: Debug logging removed
            }
        }
    }
}
