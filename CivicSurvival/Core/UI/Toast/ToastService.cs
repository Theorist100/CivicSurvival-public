using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.UI.Toast
{
    /// <summary>
    /// Service for toast notifications queue management.
    /// Pure business logic - no UI bindings.
    ///
    /// NOTE: Intentionally NO serialization - toasts are ephemeral UI notifications.
    /// Active toasts disappear on save/load BY DESIGN (not a bug).
    /// Corruption offers will regenerate naturally after load.
    /// </summary>
    [InfrastructureService]
    public sealed class ToastService : IDisposable
    {
        private static readonly LogContext Log = new("ToastService");
        // ============================================================================
        // TOAST DATA STRUCTURES
        // ============================================================================

        public struct ToastData
        {
            public int Id;
            public ToastType Type;
            public ToastPriority Priority;
            public string Title;
            public string Message;
            public string AcceptLabel;
            public string RejectLabel;
            public float CreatedTime;
            public float ExpiresTime;
            public int ContextData;
            public int DedupKey;
        }

        // ============================================================================
        // STATE
        // ============================================================================

        private readonly List<ToastData> m_ActiveToasts = new List<ToastData>();
        private readonly List<ToastData> m_PendingQueue = new List<ToastData>();
        private readonly List<int> m_ExpiredEventBuffer = new List<int>();
        private int m_NextToastId = 1;
        private float m_LastOfferGameDay = float.MinValue;
        private float m_LastPopupGameDay = float.MinValue;
        private bool m_NeedsCooldownInit = true; // FIX W1-M7: Prevent burst after load

        // Events (Action<T> is simpler than EventHandler<T> for internal events)
#pragma warning disable CA1003
        public event Action<int, bool>? OnToastInteracted;
        public event Action<int>? OnToastExpired;
#pragma warning restore CA1003

        // Time providers (abstracted for testability)
        private Func<float> m_GetGameDay;
        private Func<float> m_GetRealTime = () => Time.time;

        private static float GetGameDayDefault()
        {
            // Lazy-resolve per call: caching a per-World ECS ref stales across world
            // transitions. Static Instance getter is cheap at toast frequency.
            var provider = GameTimeSystem.Instance;
            if (provider == null) return 0f;
            return provider.Current.TotalGameHours / GameRate.HOURS_PER_DAY;
        }

        public ToastService()
        {
            m_GetGameDay = GetGameDayDefault;
        }

        public void SetGameTimeProvider(Func<float> getGameDay)
        {
            m_GetGameDay = getGameDay;
        }

        /// <summary>
        /// Set display-time provider for testability (defaults to Unity scaled time).
        /// </summary>
        public void SetRealTimeProvider(Func<float> getRealTime)
        {
            m_GetRealTime = getRealTime;
        }

        public void Dispose()
        {
            // Publish expiry before dropping ephemeral queue state so subscribers can
            // clear correlated pending ECS state.
            ExpireAllToasts();
            OnToastInteracted = null;
            OnToastExpired = null;
        }

        // ============================================================================
        // UPDATE (called by UI panel or system)
        // ============================================================================

        public void Update()
        {
            float currentTime = m_GetRealTime();

            m_ExpiredEventBuffer.Clear();

            // Process expired active toasts
            for (int i = m_ActiveToasts.Count - 1; i >= 0; i--)
            {
                var toast = m_ActiveToasts[i];

                if (!IsPersistent(toast.Priority) && currentTime >= toast.ExpiresTime)
                {
                    m_ActiveToasts.RemoveAt(i);
                    m_ExpiredEventBuffer.Add(toast.Id);
                    Log.Info($"Toast #{toast.Id} expired (type: {toast.Type})");
                }
            }

            // Drop stale pending non-critical toasts. Critical toasts persist until dismissed.
            for (int i = m_PendingQueue.Count - 1; i >= 0; i--)
            {
                var toast = m_PendingQueue[i];
                if (!IsPersistent(toast.Priority) && currentTime - toast.CreatedTime >= MAX_PENDING_SECONDS)
                {
                    m_PendingQueue.RemoveAt(i);
                    m_ExpiredEventBuffer.Add(toast.Id);
                    Log.Info($"Pending toast #{toast.Id} expired before promotion (type: {toast.Type})");
                }
            }

            PublishExpiredEvents();

            // Promote pending toasts
            while (m_PendingQueue.Count > 0 && m_ActiveToasts.Count < Engine.Toast.QUEUE_MAX)
            {
                var toast = m_PendingQueue[0];
                m_PendingQueue.RemoveAt(0);
                toast.ExpiresTime = GetExpiresTime(currentTime, toast.Priority);
                m_ActiveToasts.Add(toast);

                if (Log.IsDebugEnabled) Log.Debug($"Promoted toast #{toast.Id} from queue");
            }
        }

        // ============================================================================
        // Public API
        // ============================================================================

        public bool QueueToast(
            ToastType type,
            string title,
            string message,
            string acceptLabel = "Accept",
            string rejectLabel = "Decline",
            ToastPriority priority = ToastPriority.Normal,
            int contextData = 0,
            bool bypassOfferCooldown = false,
            int dedupKey = 0)
        {
            return QueueToastId(type, title, message, acceptLabel, rejectLabel, priority,
                contextData, bypassOfferCooldown, dedupKey) > 0;
        }

        public int QueueToastId(
            ToastType type,
            string title,
            string message,
            string acceptLabel = "Accept",
            string rejectLabel = "Decline",
            ToastPriority priority = ToastPriority.Normal,
            int contextData = 0,
            bool bypassOfferCooldown = false,
            int dedupKey = 0)
        {
            float currentGameDay = m_GetGameDay();

            // FIX W1-M7: Initialize cooldown from current game time on first call after load.
            // Day 0 must still allow the first toast, so sentinel fields carry first-call behavior.
            if (m_NeedsCooldownInit)
            {
                if (currentGameDay > 0f)
                    m_LastOfferGameDay = currentGameDay;
                m_NeedsCooldownInit = false;
            }

            // Check global popup cooldown (Critical priority bypasses - police/fraud must not be silently dropped)
            var corruptionEvents = Core.Config.BalanceConfig.Current.CorruptionEvents;
            if (priority != ToastPriority.Critical
                && currentGameDay - m_LastPopupGameDay < corruptionEvents.MinDaysBetweenAnyPopup)
            {
                Log.Info($"Cooldown active, skipping {type}");
                return 0;
            }

            // Check offer-specific cooldown
            if (!bypassOfferCooldown && IsProactiveOffer(type) &&
                currentGameDay - m_LastOfferGameDay < corruptionEvents.MinDaysBetweenOffers)
            {
                Log.Info($"Offer cooldown active, skipping {type}");
                return 0;
            }

            int effectiveDedupKey = dedupKey != 0
                ? dedupKey
                : ComputeDedupKey(type, title, message, contextData);
            if (TryRefreshDuplicate(effectiveDedupKey, priority, out int existingId))
                return existingId;

            // Non-critical toasts keep the old bounded queue contract. Critical toasts
            // can preempt lower priority active toasts or wait in pending instead of dropping.
            int totalCount = m_ActiveToasts.Count + m_PendingQueue.Count;
            if (priority != ToastPriority.Critical && totalCount >= Engine.Toast.QUEUE_MAX)
            {
                Log.Info($"Queue full ({totalCount}), skipping {type}");
                return 0;
            }

            float currentTime = m_GetRealTime();
            int toastId = m_NextToastId;
            var toast = new ToastData
            {
                Id = toastId,
                Type = type,
                Priority = priority,
                Title = title,
                Message = message,
                AcceptLabel = acceptLabel,
                RejectLabel = rejectLabel,
                CreatedTime = currentTime,
                ExpiresTime = GetExpiresTime(currentTime, priority),
                ContextData = contextData,
                DedupKey = effectiveDedupKey
            };

            bool addedActive = false;
            if (priority == ToastPriority.Critical)
            {
                if (m_ActiveToasts.Count >= Engine.Toast.QUEUE_MAX)
                    PreemptLowestNonCriticalToast();

                if (m_ActiveToasts.Count < Engine.Toast.QUEUE_MAX)
                {
                    m_ActiveToasts.Add(toast);
                    addedActive = true;
                    Log.Info($"Added critical toast #{toast.Id}: {type}");
                }
            }

            if (!addedActive)
            {
                toast.ExpiresTime = float.MaxValue;
                m_PendingQueue.Add(toast);
                if (Log.IsDebugEnabled) Log.Debug($"Queued toast #{toast.Id}: {type}");
            }

            m_NextToastId = toastId + 1;
            m_LastPopupGameDay = currentGameDay;
            if (IsProactiveOffer(type)) m_LastOfferGameDay = currentGameDay;

            return toastId;
        }

        public bool HasActiveToasts => m_ActiveToasts.Count > 0;
        public int ActiveToastCount => m_ActiveToasts.Count;

        // ============================================================================
        // TOAST ACTIONS (called by UI triggers)
        // ============================================================================

        public void AcceptToast(int toastId)
        {
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                if (m_ActiveToasts[i].Id == toastId)
                {
                    var toast = m_ActiveToasts[i];
                    m_ActiveToasts.RemoveAt(i);

                    OnToastInteracted?.Invoke(toastId, true);
                    Log.Info($"Toast #{toastId} accepted (type: {toast.Type})");
                    return;
                }
            }
        }

        public void RejectToast(int toastId)
        {
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                if (m_ActiveToasts[i].Id == toastId)
                {
                    var toast = m_ActiveToasts[i];
                    m_ActiveToasts.RemoveAt(i);

                    OnToastInteracted?.Invoke(toastId, false);
                    Log.Info($"Toast #{toastId} rejected (type: {toast.Type})");
                    return;
                }
            }
        }

        public void DismissToast(int toastId)
        {
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                if (m_ActiveToasts[i].Id == toastId)
                {
                    var toast = m_ActiveToasts[i];
                    m_ActiveToasts.RemoveAt(i);

                    m_ExpiredEventBuffer.Clear();
                    m_ExpiredEventBuffer.Add(toastId);
                    PublishExpiredEvents();
                    Log.Info($"Toast #{toastId} dismissed (type: {toast.Type})");
                    return;
                }
            }
        }

        // ============================================================================
        // JSON SERIALIZATION (for UI binding)
        // ============================================================================

        public string GetToastsJson()
        {
            if (m_ActiveToasts.Count == 0) return JsonBuilder.EmptyArray;

            float currentTime = m_GetRealTime();
            var sb = new StringBuilder(512);
            sb.Append('[');
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var toast = m_ActiveToasts[i];
                bool persistent = IsPersistent(toast.Priority);
                float remainingSeconds = persistent ? float.MaxValue : math.max(0, toast.ExpiresTime - currentTime);
                float displayTime = GetDisplaySeconds(toast.Priority);
                float progress = persistent ? 0f : 1f - (remainingSeconds / math.max(displayTime, 0.001f));

                var dto = new ToastDataDto
                {
                    Id = toast.Id,
                    Type = toast.Type.ToString(),
                    Priority = (int)toast.Priority,
                    Title = toast.Title ?? string.Empty,
                    Message = toast.Message ?? string.Empty,
                    AcceptLabel = toast.AcceptLabel ?? string.Empty,
                    RejectLabel = toast.RejectLabel ?? string.Empty,
                    RemainingSeconds = remainingSeconds,
                    Progress = progress,
                    ContextData = toast.ContextData,
                };
                dto.WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        private bool IsProactiveOffer(ToastType type)
        {
            return type == ToastType.ProcurementOffer;
        }

        private const float LOW_PRIORITY_DISPLAY_SECONDS = 30f;
        private const float HIGH_PRIORITY_DISPLAY_SECONDS = 60f;
        private const float CRITICAL_PRIORITY_DISPLAY_SECONDS = float.MaxValue;
        private const float MAX_PENDING_SECONDS = 300f;
        private const int DEDUP_HASH_SEED = 17;
        private const int DEDUP_HASH_MULTIPLIER = 31;

        private static float GetDisplaySeconds(ToastPriority priority) => priority switch
        {
            ToastPriority.Low => LOW_PRIORITY_DISPLAY_SECONDS,
            ToastPriority.Normal => Engine.Toast.DISPLAY_SECONDS,  // 45s
            ToastPriority.High => HIGH_PRIORITY_DISPLAY_SECONDS,
            ToastPriority.Critical => CRITICAL_PRIORITY_DISPLAY_SECONDS,
            _ => HIGH_PRIORITY_DISPLAY_SECONDS  // safe fallback for unknown priorities
        };

        private static bool IsPersistent(ToastPriority priority) => priority == ToastPriority.Critical;

        private static float GetExpiresTime(float currentTime, ToastPriority priority)
        {
            return IsPersistent(priority) ? float.MaxValue : currentTime + GetDisplaySeconds(priority);
        }

        private static int ComputeDedupKey(ToastType type, string title, string message, int contextData)
        {
            unchecked
            {
                int hash = DEDUP_HASH_SEED;
                hash = hash * DEDUP_HASH_MULTIPLIER + (int)type;
                hash = hash * DEDUP_HASH_MULTIPLIER + contextData;
                hash = hash * DEDUP_HASH_MULTIPLIER + StringComparer.Ordinal.GetHashCode(title ?? string.Empty);
                hash = hash * DEDUP_HASH_MULTIPLIER + StringComparer.Ordinal.GetHashCode(message ?? string.Empty);
                return hash == 0 ? 1 : hash;
            }
        }

        private bool TryRefreshDuplicate(int dedupKey, ToastPriority priority, out int existingId)
        {
            float currentTime = m_GetRealTime();
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                var toast = m_ActiveToasts[i];
                if (toast.DedupKey != dedupKey) continue;

                toast.ExpiresTime = GetExpiresTime(currentTime, priority);
                m_ActiveToasts[i] = toast;
                existingId = toast.Id;
                return true;
            }

            for (int i = 0; i < m_PendingQueue.Count; i++)
            {
                var toast = m_PendingQueue[i];
                if (toast.DedupKey != dedupKey) continue;

                toast.CreatedTime = currentTime;
                m_PendingQueue[i] = toast;
                existingId = toast.Id;
                return true;
            }

            existingId = 0;
            return false;
        }

        private void PreemptLowestNonCriticalToast()
        {
            int index = -1;
            ToastPriority lowestPriority = ToastPriority.Critical;
            for (int i = 0; i < m_ActiveToasts.Count; i++)
            {
                var candidate = m_ActiveToasts[i];
                if (candidate.Priority >= ToastPriority.Critical || candidate.Priority > lowestPriority)
                    continue;

                lowestPriority = candidate.Priority;
                index = i;
            }

            if (index < 0)
                return;

            var preempted = m_ActiveToasts[index];
            m_ActiveToasts.RemoveAt(index);
            m_ExpiredEventBuffer.Clear();
            m_ExpiredEventBuffer.Add(preempted.Id);
            PublishExpiredEvents();
            Log.Info($"Toast #{preempted.Id} preempted by critical notification (type: {preempted.Type})");
        }

        private void ExpireAllToasts()
        {
            m_ExpiredEventBuffer.Clear();
            for (int i = 0; i < m_ActiveToasts.Count; i++)
                m_ExpiredEventBuffer.Add(m_ActiveToasts[i].Id);
            for (int i = 0; i < m_PendingQueue.Count; i++)
                m_ExpiredEventBuffer.Add(m_PendingQueue[i].Id);

            m_ActiveToasts.Clear();
            m_PendingQueue.Clear();
            PublishExpiredEvents();
        }

        private void PublishExpiredEvents()
        {
            if (m_ExpiredEventBuffer.Count == 0)
                return;

            var handler = OnToastExpired;
            if (handler != null)
            {
                for (int i = 0; i < m_ExpiredEventBuffer.Count; i++)
                    handler(m_ExpiredEventBuffer[i]);
            }
            m_ExpiredEventBuffer.Clear();
        }

    }
}
