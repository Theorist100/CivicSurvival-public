using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using System.Collections.Generic;
using System.Text;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Centralized modal coordination across all systems.
    /// Owns active modal priority, event-time payload, and pending queue; UI consumes its snapshot.
    /// </summary>
    public sealed class ModalCoordinator
    {
        public static readonly ModalCoordinator Instance = new();

        private static readonly LogContext Log = new("ModalCoordinator");

        private readonly object m_Lock = new();
        private readonly List<ModalEntry> m_Pending = new();
        private string? m_ActiveId;
        private string m_ActivePayloadJson = JsonBuilder.EmptyObject;
        private readonly VersionedView<ModalSnapshot> m_SnapshotView = new(ModalSnapshot.Empty);
        private int m_SnapshotSerial;
        private bool m_EventBusMissingWarned;

        private readonly struct ModalEntry
        {
            public ModalEntry(string id, string payloadJson)
            {
                Id = id;
                PayloadJson = payloadJson;
            }

            public string Id { get; }
            public string PayloadJson { get; }
        }

        /// <summary>
        /// Try to acquire the modal slot with an empty payload.
        /// Idempotent: returns true if the same id already holds the slot.
        /// </summary>
        public bool TryShow(string id)
        {
            return TryShow(id, JsonBuilder.EmptyObject);
        }

        /// <summary>
        /// Try to acquire the modal slot with an event-time DTO payload.
        /// </summary>
        public bool TryShow(string id, IDomainDto payload)
        {
            if (payload == null)
                return TryShow(id, JsonBuilder.EmptyObject);

            var sb = new StringBuilder(1024);
            payload.WriteTo(sb);
            return TryShow(id, sb.ToString());
        }

        /// <summary>
        /// Try to acquire the modal slot with a pre-serialized event-time payload.
        /// </summary>
        public bool TryShow(string id, string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            payloadJson = string.IsNullOrWhiteSpace(payloadJson) ? JsonBuilder.EmptyObject : payloadJson;

            string? shownId = null;
            bool accepted = false;
            lock (m_Lock)
            {
                if (m_ActiveId == id)
                    return true;

                if (m_ActiveId == null)
                {
                    RemovePending(id);
                    m_ActiveId = id;
                    m_ActivePayloadJson = payloadJson;
                    PublishLocked();
                    shownId = id;
                    accepted = true;
                }
                else if (ModalPriority.Get(id) > ModalPriority.Get(m_ActiveId))
                {
                    if (!ContainsPending(m_ActiveId))
                        m_Pending.Add(new ModalEntry(m_ActiveId, m_ActivePayloadJson));
                    RemovePending(id);
                    m_ActiveId = id;
                    m_ActivePayloadJson = payloadJson;
                    PublishLocked();
                    shownId = id;
                    accepted = true;
                }
                else
                {
                    if (!ContainsPending(id))
                    {
                        m_Pending.Add(new ModalEntry(id, payloadJson));
                        PublishLocked();
                    }
                }
            }

            if (shownId != null)
            {
                Log.Info($"Show: {shownId}");
                PublishActivated(shownId);
            }
            return accepted;
        }

        /// <summary>
        /// Release the modal slot or remove a queued modal.
        /// </summary>
        public void Dismiss(string id)
        {
            bool dismissed = false;
            bool removedQueued = false;
            string? promotedId = null;
            lock (m_Lock)
            {
                removedQueued = RemovePending(id);
                if (m_ActiveId != id)
                {
                    if (removedQueued)
                        PublishLocked();
                    return;
                }

                var promoted = PopHighestPriorityPending();
                promotedId = promoted?.Id;
                m_ActiveId = promotedId;
                m_ActivePayloadJson = promoted?.PayloadJson ?? JsonBuilder.EmptyObject;
                PublishLocked();
                dismissed = true;
            }

            if (dismissed)
            {
                Log.Info($"Dismiss: {id}");
                if (promotedId != null)
                {
                    Log.Info($"Show: {promotedId}");
                    PublishActivated(promotedId);
                }
            }
        }

        /// <summary>Whether any modal is currently active.</summary>
        public bool IsAnyActive { get { lock (m_Lock) return m_ActiveId != null; } }

        /// <summary>The currently active modal id, or null.</summary>
        public string? ActiveId { get { lock (m_Lock) return m_ActiveId; } }

        public IVersionedView<ModalSnapshot> SnapshotView => m_SnapshotView;

        public string SnapshotJson
        {
            get
            {
                lock (m_Lock)
                {
                    return BuildSnapshotJsonLocked();
                }
            }
        }

        /// <summary>
        /// Scoped dismiss: dismiss each id without clearing the queue or publishing ModalResetEvent.
        /// Use on lifecycle transitions that should only remove a known set of modals
        /// (e.g. Crisis exit clearing Crisis tutorial modals while leaving global modals intact).
        /// Full Reset() stays reserved for load/new-game.
        /// </summary>
        public void DismissMany(params string[] ids)
        {
            if (ids == null || ids.Length == 0)
                return;
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!string.IsNullOrWhiteSpace(id))
                    Dismiss(id);
            }
        }

        /// <summary>Reset coordinator state (new game / load). Publishes ModalResetEvent.</summary>
        public void Reset()
        {
            string? previousId;
            lock (m_Lock)
            {
                previousId = m_ActiveId;
                m_ActiveId = null;
                m_ActivePayloadJson = JsonBuilder.EmptyObject;
                m_Pending.Clear();
                PublishLocked();
            }

            if (previousId != null)
                Log.Info($"Reset (was: {previousId})");
            var eventBus = GetEventBus();
            if (eventBus != null)
                eventBus.SafePublishSilent(new ModalResetEvent());
        }

        private void PublishActivated(string id)
        {
            var eventBus = GetEventBus();
            if (eventBus != null)
                eventBus.SafePublishSilent(new ModalActivatedEvent(id));
        }

        private IEventBus? GetEventBus()
        {
            var eventBus = ServiceRegistry.TryGet<IEventBus>();
            if (eventBus == null && !m_EventBusMissingWarned)
            {
                m_EventBusMissingWarned = true;
                Log.Warn("EventBus unavailable; modal lifecycle event was not published");
            }
            else if (eventBus != null)
            {
                m_EventBusMissingWarned = false;
            }

            return eventBus;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private void PublishLocked()
        {
            m_SnapshotSerial = m_SnapshotSerial == int.MaxValue ? 1 : m_SnapshotSerial + 1;
            m_SnapshotView.Publish(new ModalSnapshot(BuildSnapshotJsonLocked()));
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private string BuildSnapshotJsonLocked()
        {
            var dto = new ModalSnapshotDto();
            dto.ActiveId = m_ActiveId ?? string.Empty;
            dto.ActivePriority = m_ActiveId == null ? 0 : ModalPriority.Get(m_ActiveId);
            dto.ActiveDataJson = m_ActiveId == null ? "null" : m_ActivePayloadJson;
            dto.QueueJson = BuildQueueJsonLocked();
            dto.Version = m_SnapshotSerial;

            var sb = new StringBuilder(1024);
            dto.WriteTo(sb);
            return sb.ToString();
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private string BuildQueueJsonLocked()
        {
            if (m_Pending.Count == 0)
                return "[]";

            var sb = new StringBuilder(64);
            sb.Append('[');
            for (int i = 0; i < m_Pending.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                AppendJsonEscaped(sb, m_Pending[i].Id);
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendJsonEscaped(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private bool ContainsPending(string id)
        {
            for (int i = 0; i < m_Pending.Count; i++)
            {
                if (m_Pending[i].Id == id)
                    return true;
            }

            return false;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private bool RemovePending(string id)
        {
            for (int i = 0; i < m_Pending.Count; i++)
            {
                if (m_Pending[i].Id == id)
                {
                    m_Pending.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        [CallerHoldsLock(nameof(m_Lock))]
        private ModalEntry? PopHighestPriorityPending()
        {
            if (m_Pending.Count == 0)
                return null;

            int bestIndex = 0;
            int bestPriority = ModalPriority.Get(m_Pending[0].Id);
            for (int i = 1; i < m_Pending.Count; i++)
            {
                int priority = ModalPriority.Get(m_Pending[i].Id);
                if (priority > bestPriority)
                {
                    bestIndex = i;
                    bestPriority = priority;
                }
            }

            var entry = m_Pending[bestIndex];
            m_Pending.RemoveAt(bestIndex);
            return entry;
        }
    }
}
