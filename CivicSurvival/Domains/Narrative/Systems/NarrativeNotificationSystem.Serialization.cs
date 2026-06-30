using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using Unity.Entities;

namespace CivicSurvival.Domains.Narrative.Systems
{
    /// <summary>
    /// NarrativeNotificationSystem - Save/Load serialization (IDefaultSerializable).
    /// FIX NAR-P2-005: Flushes all pending batches before save to prevent notification loss.
    /// </summary>
    public partial class NarrativeNotificationSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();
        void IResettable.ResetState() => ResetState();

        private void ResetState()
        {
            // Reset all resolver state (investigation/police flags, pending batches, entity refs).
            // Resolvers without state use the default no-op INarrativeResolver.Reset().
            for (int i = 0; i < m_AllResolvers.Length; i++)
                m_AllResolvers[i].Reset();

            // Unsubscribe old resolvers immediately so no events reach stale instances
            // during the gap between ResetState() and the next Initialize() call.
            // Initialize() also unsubscribes as defense-in-depth; Unsubscribe() is idempotent.
            for (int i = 0; i < m_AllResolvers.Length; i++)
                m_AllResolvers[i].Unsubscribe();
            UnsubscribeSafe<CivicSurvival.Core.Events.NarrativeTriggerEvent>(OnNarrativeTrigger);
            m_PendingTriggers.Clear();
            m_PendingTriggerHead = 0;
            m_PendingToasts.Clear();
            m_PendingToastHead = 0;
            m_EnqueuedKeys.Clear();
            m_TimeSystem = null;
            m_Sink = null;
            m_ResolverProfileKeys = System.Array.Empty<string>();

            // Force re-initialization on next OnStartRunning (resolvers recreated with fresh state)
            m_Initialized = false;
            m_NeedNotifyDeserialized = false;
            m_HasDeserializedPendingTriggers = false;
            m_HasDeserializedPendingToasts = false;
            m_HasDeserializedResolverState = false;
            m_DeserializedResolverState = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                CaptureResolverFlushes();
                WritePendingNarrativeState(writer);

                // DrainAll removed: calling ProcessCallback (→ PushSystemAlert → vanilla UI mutation)
                // during serialization context is unsafe. NotificationSystem clears the transient
                // queue on game load so pre-save cosmetic notifications do not leak into the
                // loaded world.

                Log.Debug("Captured pending narrative batches before save");

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(NarrativeNotificationSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, nameof(NarrativeNotificationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                // Force re-initialization: resolvers must be re-created with fresh state after load
                m_Initialized = false;
                m_NeedNotifyDeserialized = true;
                m_HasDeserializedPendingTriggers = false;
                m_HasDeserializedPendingToasts = false;
                m_HasDeserializedResolverState = false;

                m_PendingTriggers.Clear();
                m_PendingTriggerHead = 0;
                m_PendingToasts.Clear();
                m_PendingToastHead = 0;
                m_EnqueuedKeys.Clear();

                NarrativeNotificationCodec.Read(
                    reader,
                    MAX_PENDING_TRIGGERS,
                    MAX_PENDING_TOASTS,
                    maxContextEntries: 16,
                    out var state);
                ApplyPendingTriggers(state.PendingTriggers);
                ApplyPendingToasts(state.PendingToasts);
                m_DeserializedResolverState = state.ResolverState;
                m_HasDeserializedResolverState = state.HasResolverState;

                if (Log.IsDebugEnabled) Log.Debug($"Deserialized v{version}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private void CaptureResolverFlushes()
        {
            if (m_AllResolvers.Length == 0)
                return;

            var sink = m_Sink;
            if (sink == null)
            {
                Log.Warn("NotificationState unavailable during save; resolver batches retained for runtime flush");
                return;
            }

            var captured = new List<NarrativeToastDto>();
            sink.CapturePushes(captured, FlushAllResolvers);
            for (int i = 0; i < captured.Count; i++)
                EnqueuePendingToast(captured[i]);
        }

        private void FlushAllResolvers()
        {
            for (int i = 0; i < m_AllResolvers.Length; i++)
            {
                m_AllResolvers[i].FlushAll();
            }
        }

        private void EnqueuePendingToast(NarrativeToastDto toast)
        {
            if (m_PendingToasts.Count - m_PendingToastHead >= MAX_PENDING_TOASTS)
            {
                m_PendingToastHead++;
                CompactPendingToastsIfNeeded();
            }
            m_PendingToasts.Add(toast);
        }

        private void WritePendingNarrativeState<TWriter>(TWriter writer) where TWriter : IWriter
        {
            bool hasResolverState = m_Initialized || m_HasDeserializedResolverState;
            var state = new NarrativeNotificationPersistState(
                CapturePendingTriggers(),
                CapturePendingToasts(),
                m_Initialized ? CaptureResolverState() : m_DeserializedResolverState,
                hasResolverState);
            NarrativeNotificationCodec.Write(state, writer);
        }

        private NarrativeTriggerPersistState[] CapturePendingTriggers()
        {
            int count = m_PendingTriggers.Count - m_PendingTriggerHead;
            var triggers = new NarrativeTriggerPersistState[count];
            int targetIndex = 0;
            for (int i = m_PendingTriggerHead; i < m_PendingTriggers.Count; i++)
            {
                var evt = m_PendingTriggers[i].Event;
                triggers[targetIndex++] = new NarrativeTriggerPersistState(
                    evt.TriggerKey,
                    ToContextEntries(evt.ContextData),
                    m_PendingTriggers[i].EnqueuedGameTimeSeconds);
            }

            return triggers;
        }

        private NarrativeToastPersistState[] CapturePendingToasts()
        {
            int count = m_PendingToasts.Count - m_PendingToastHead;
            var toasts = new NarrativeToastPersistState[count];
            int targetIndex = 0;
            for (int i = m_PendingToastHead; i < m_PendingToasts.Count; i++)
            {
                var toast = m_PendingToasts[i];
                toasts[targetIndex++] = new NarrativeToastPersistState(
                    (int)toast.Channel,
                    toast.Id,
                    toast.Title,
                    toast.Message,
                    (int)toast.Mood,
                    (int)toast.Status);
            }

            return toasts;
        }

        private void ApplyPendingTriggers(IReadOnlyList<NarrativeTriggerPersistState> pendingTriggers)
        {
            for (int i = 0; i < pendingTriggers.Count; i++)
            {
                var trigger = pendingTriggers[i];
                if (string.IsNullOrEmpty(trigger.TriggerKey)) continue;
                var evt = new NarrativeTriggerEvent(trigger.TriggerKey, ToContextDictionary(trigger.Context));
                string coalescingKey = BuildCoalescingKey(evt);
                if (!m_EnqueuedKeys.Add(coalescingKey)) continue;
                m_PendingTriggers.Add(new PendingNarrativeTrigger(evt, coalescingKey, trigger.EnqueuedGameTimeSeconds));
                m_HasDeserializedPendingTriggers = true;
            }
        }

        private void ApplyPendingToasts(IReadOnlyList<NarrativeToastPersistState> pendingToasts)
        {
            for (int i = 0; i < pendingToasts.Count; i++)
            {
                var toast = pendingToasts[i];
                if (string.IsNullOrEmpty(toast.Id)) continue;
                // Only SystemAlert toasts travel through the sink now (news/social are
                // published directly as events). A legacy save may carry a non-zero
                // (old SocialSatire) channel int — coerce it to SystemAlert.
                EnqueuePendingToast(new NarrativeToastDto(
                    NotificationType.SystemAlert,
                    toast.Id,
                    toast.Title,
                    toast.Message,
                    ToSocialMood(toast.Mood),
                    ToNotificationStatus(toast.Status)));
                m_HasDeserializedPendingToasts = true;
            }
        }

        private static SocialMood ToSocialMood(int value)
        {
            if (!System.Enum.IsDefined(typeof(SocialMood), value))
                return SocialMood.Neutral;
            return (SocialMood)value;
        }

        private static NotificationStatus ToNotificationStatus(int value)
        {
            if (!System.Enum.IsDefined(typeof(NotificationStatus), value))
                return NotificationStatus.Info;
            return (NotificationStatus)value;
        }

        private static StringStringPersistEntry[] ToContextEntries(IReadOnlyDictionary<string, string> context)
        {
            var entries = new StringStringPersistEntry[context.Count];
            int index = 0;
            foreach (var kvp in context)
                entries[index++] = new StringStringPersistEntry(kvp.Key, kvp.Value);
            return entries;
        }

        private static Dictionary<string, string> ToContextDictionary(IReadOnlyList<StringStringPersistEntry> entries)
        {
            var context = new Dictionary<string, string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
                context[entries[i].Key] = entries[i].Value;
            return context;
        }
    }
}
