using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Simple synchronous event bus for game events.
    ///
    /// THREADING MODEL: Main-thread only.
    /// ✅ Safe to use from: SystemBase.OnUpdate, UISystemBase, MonoBehaviour, React triggers
    /// ❌ NOT compatible with: IJobChunk, IJobEntity, Burst-compiled code
    ///
    /// If you need to publish events from Jobs, use deferred pattern:
    /// 1. In Job: write to NativeQueue or add IComponentData marker
    /// 2. In SystemBase.OnUpdate: read queue/query and call EventBus.Publish()
    ///
    /// The lock exists only to protect Subscribe/Unsubscribe during initialization,
    /// not for multi-threaded Publish (which would require lock-free design).
    ///
    /// ⚠️ MEMORY LEAK WARNING: Handlers are stored with STRONG references.
    /// You MUST call Unsubscribe() in OnDestroy/Dispose, or the handler's
    /// target object will never be garbage collected. Example:
    ///   OnCreate(): eventBus.Subscribe&lt;MyEvent&gt;(OnMyEvent);
    ///   OnDestroy(): eventBus.Unsubscribe&lt;MyEvent&gt;(OnMyEvent);
    /// </summary>
    public class EventBus : IEventBus
    {
        private static readonly LogContext Log = new("EventBus");

        // FIX S6-07/S6-02: List preserves insertion order (deterministic OnCreate order).
        // HashSet had non-deterministic iteration → budget starvation when debt handler
        // ran after fire damage handler for DayChangedEvent.
        // Priority field: lower number = dispatched first. Default = 100.
        private readonly Dictionary<Type, List<SubscriberEntry>> m_Subscribers = new();
        private readonly Dictionary<string, BufferedSubscriberGroup> m_BufferedGroups = new();
        private const int DEFAULT_PRIORITY = 100;
        private readonly object m_Lock = new();
        private long m_NextBufferedArrivalOrdinal;

        /// <summary>
        /// Per-subscriber watermarks for ISequencedEvent dedup.
        /// Key: "EventTypeName:SubscriberTypeName", Value: last handled sequence.
        /// Prevents duplicate event delivery (e.g., DayChanged re-fire on load).
        /// In-memory only — existing DayChangedDedup provides cross-load persistence.
        /// </summary>
        private readonly Dictionary<string, long> m_Watermarks = new();

        // PERF: Cache event type names to avoid allocation in Publish()
        private readonly ConcurrentDictionary<Type, string> m_EventNameCache = new();

        // PERF: Cache profiler metric names (Event.{Type}.{Subscriber}) to avoid string allocation
        private readonly ConcurrentDictionary<(Type, Type, string), string> m_ProfilerNameCache = new();

        // Cycle detection: tracks event types currently being published (main-thread only)
        private readonly HashSet<Type> m_PublishingStack = new();

        // FIX S9-01: Defer ActChangedEvent when published during DayChanged dispatch.
        // Prevents "dual-context day" where early handlers see PreWar, late handlers see Crisis.
        // Publish is main-thread-only; the lock protects subscriber storage, not dispatch state.
#pragma warning disable CIVIC114
        private bool m_InDayChangedDispatch;
        private readonly List<ActChangedEvent> m_DeferredActChanged = new();
        private readonly List<HeritageGrantedEvent> m_DeferredHeritageGranted = new();
#pragma warning restore CIVIC114
        private static readonly Type s_DayChangedType = typeof(DayChangedEvent);
        private static readonly Type s_ActChangedType = typeof(ActChangedEvent);
        private static readonly Type s_HeritageGrantedType = typeof(HeritageGrantedEvent);

        // Thread safety assertion: capture main thread ID at construction
        private readonly int m_MainThreadId = System.Environment.CurrentManagedThreadId;
        private bool m_DispatchCancelled;
        private int m_BufferedDrainDepth;

        #if DEBUG
        /// <summary>Enable detailed logging of all events (toggle via Settings)</summary>
        public bool EnableLogging { get; set; }

        /// <summary>Event history for debug panel (List as queue substitute)</summary>
        private readonly List<EventRecord> m_History = new();
        private const int MAX_HISTORY = 100;

        public IReadOnlyCollection<EventRecord> GetHistory() => m_History;
        public void ClearHistory() => m_History.Clear();
        #endif

        public void Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            SubscribeInternal(handler, DEFAULT_PRIORITY);
        }

        public void Subscribe<T>(Action<T> handler, int priority) where T : IGameEvent
        {
            SubscribeInternal(handler, priority);
        }

        private void SubscribeInternal<T>(Action<T> handler, int priority) where T : IGameEvent
        {
            string? duplicatePriorityWarning = null;
            #if DEBUG
            bool duplicateSubscription = false;
            string debugSubscriber = null!;
            string debugTypeName = null!;
            #endif
            lock (m_Lock)
            {
                var type = typeof(T);
                if (!m_Subscribers.TryGetValue(type, out var set))
                {
                    set = new List<SubscriberEntry>();
                    m_Subscribers[type] = set;
                }
                // Prevent duplicate subscriptions (compare Delegate only, ignore priority)
                int duplicateIdx = set.FindIndex(s => s.Handler.Equals(handler));
                if (duplicateIdx >= 0)
                {
                    #if DEBUG
                    duplicateSubscription = true;
                    #endif
                    if (set[duplicateIdx].Priority != priority)
                    {
                        duplicatePriorityWarning = $"Duplicate subscription ignored for {type.Name}.{handler.Method.Name}: existing priority={set[duplicateIdx].Priority}, requested priority={priority}";
                    }
                }
                else
                {
                    var entry = new SubscriberEntry
                    {
                        Handler = handler,
                        Priority = priority,
                        WatermarkKey = CreateWatermarkKey(type, handler)
                    };
                    InsertByPriorityStable(set, entry);

                    #if DEBUG
                    debugSubscriber = handler.Target?.GetType().Name ?? "static";
                    debugTypeName = type.Name;
                    #endif
                }
            }
            if (duplicatePriorityWarning != null)
                Log.Warn(duplicatePriorityWarning);
            #if DEBUG
            if (!duplicateSubscription && debugSubscriber != null && Log.IsDebugEnabled) Log.Debug($"{debugSubscriber} subscribed to {debugTypeName} (priority={priority})");
            #endif
        }

        public void SubscribeBuffered<T>(
            Action<T> handler,
            int priority,
            string subscriberKey,
            Func<bool> isReady,
            int capacity = 1024)
            where T : IGameEvent
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(subscriberKey))
                throw new ArgumentException("subscriberKey is required", nameof(subscriberKey));
            if (isReady == null)
                throw new ArgumentNullException(nameof(isReady));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            string? duplicatePriorityWarning = null;
            string? capacityMismatchWarning = null;
            #if DEBUG
            bool duplicateSubscription = false;
            #endif
            lock (m_Lock)
            {
                var type = typeof(T);
                if (!m_Subscribers.TryGetValue(type, out var set))
                {
                    set = new List<SubscriberEntry>();
                    m_Subscribers[type] = set;
                }

                int duplicateIdx = set.FindIndex(s => s.Handler.Equals(handler));
                if (duplicateIdx >= 0)
                {
                    #if DEBUG
                    duplicateSubscription = true;
                    #endif
                    if (set[duplicateIdx].Priority != priority)
                    {
                        duplicatePriorityWarning = $"Duplicate buffered subscription ignored for {type.Name}.{handler.Method.Name}: existing priority={set[duplicateIdx].Priority}, requested priority={priority}";
                    }
                }
                else
                {
                    if (!m_BufferedGroups.TryGetValue(subscriberKey, out var group))
                    {
                        group = new BufferedSubscriberGroup
                        {
                            SubscriberKey = subscriberKey,
                            IsReady = isReady,
                            Capacity = capacity
                        };
                        m_BufferedGroups[subscriberKey] = group;
                    }
                    else if (capacity > group.Capacity)
                    {
                        #if DEBUG
                        if (!group.CapacityMismatchWarned)
                        {
                            group.CapacityMismatchWarned = true;
                            capacityMismatchWarning = $"Buffered subscriber {subscriberKey} reused with different capacities; using max capacity";
                        }
                        #endif
                        group.Capacity = capacity;
                    }
                    #if DEBUG
                    else if (capacity != group.Capacity && !group.CapacityMismatchWarned)
                    {
                        group.CapacityMismatchWarned = true;
                        capacityMismatchWarning = $"Buffered subscriber {subscriberKey} reused with different capacities; using max capacity";
                    }
                    #endif

                    var entry = new SubscriberEntry
                    {
                        Handler = handler,
                        Priority = priority,
                        WatermarkKey = CreateWatermarkKey(type, subscriberKey, handler),
                        BufferedGroup = group,
                        TypedDispatcher = evt => handler((T)evt)
                    };
                    InsertByPriorityStable(set, entry);
                }
            }

            if (duplicatePriorityWarning != null)
                Log.Warn(duplicatePriorityWarning);
            if (capacityMismatchWarning != null)
                Log.Warn(capacityMismatchWarning);

            #if DEBUG
            if (!duplicateSubscription && Log.IsDebugEnabled) Log.Debug($"{subscriberKey} buffered-subscribed to {typeof(T).Name} (priority={priority})");
            #endif
        }

        private static string? CreateWatermarkKey<T>(Type eventType, Action<T> handler) where T : IGameEvent
        {
            if (!typeof(ISequencedEvent).IsAssignableFrom(eventType))
                return null;

            // M66 FIX: Use FullName to prevent watermark key collision when two classes
            // with the same simple Name (different namespaces) subscribe to the same event.
            var subscriberName = handler.Target?.GetType().FullName ?? "static";
            return eventType.FullName + ":" + subscriberName + "." + handler.Method.Name;
        }

        private static string? CreateWatermarkKey<T>(Type eventType, string subscriberKey, Action<T> handler) where T : IGameEvent
        {
            if (!typeof(ISequencedEvent).IsAssignableFrom(eventType))
                return null;

            return eventType.FullName + ":" + subscriberKey + "." + handler.Method.Name;
        }

        private static void InsertByPriorityStable(List<SubscriberEntry> set, SubscriberEntry entry)
        {
            int insertIdx = set.Count;
            for (int i = 0; i < set.Count; i++)
            {
                if (set[i].Priority > entry.Priority)
                {
                    insertIdx = i;
                    break;
                }
            }

            set.Insert(insertIdx, entry);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
        {
            #if DEBUG
            string debugUnsub = null!;
            string debugUnsubType = null!;
            #endif
            lock (m_Lock)
            {
                if (m_Subscribers.TryGetValue(typeof(T), out var set))
                {
                    var removed = set.Where(s => s.Handler.Equals(handler)).ToArray();
                    set.RemoveAll(s => s.Handler.Equals(handler));

                    foreach (var entry in removed)
                    {
                        var group = entry.BufferedGroup;
                        if (group == null || group.Pending.Count == 0)
                            continue;

                        var kept = new BufferedPendingDeliveryQueue(group.Pending.Count);
                        while (group.Pending.Count > 0)
                        {
                            var pending = group.Pending.Dequeue();
                            if (!ReferenceEquals(pending.Entry, entry))
                                kept.Enqueue(pending);
                        }
                        group.Pending = kept;
                    }

                    RemoveEmptyBufferedGroups();

                    #if DEBUG
                    debugUnsub = handler.Target?.GetType().Name ?? "static";
                    debugUnsubType = typeof(T).Name;
                    #endif
                }
            }
            #if DEBUG
            if (debugUnsub != null && Log.IsDebugEnabled) Log.Debug($"{debugUnsub} unsubscribed from {debugUnsubType}");
            #endif
        }

        public bool Publish<T>(T evt) where T : IGameEvent
        {
            if (!IsMainThread())
            {
                Log.Error($"EventBus.Publish<{typeof(T).Name}> called from non-main thread (tid={System.Environment.CurrentManagedThreadId}, expected={m_MainThreadId})");
                Utils.DiagnosticTracker.IncrementError("EventBus.WrongThread");
                return false;
            }

            var eventType = typeof(T);

            // FIX S9-01: Defer ActChangedEvent if published during DayChanged dispatch.
            // All DayChanged handlers see the same act — deferred ActChanged fires after.
            if (eventType == s_ActChangedType && m_InDayChangedDispatch)
            {
                m_DeferredActChanged.Add((ActChangedEvent)(object)evt);
                Log.Info("Deferred ActChangedEvent (inside DayChanged dispatch)");
                return true;
            }

            if (eventType == s_HeritageGrantedType && m_InDayChangedDispatch)
            {
                m_DeferredHeritageGranted.Add((HeritageGrantedEvent)(object)evt);
                Log.Info("Deferred HeritageGrantedEvent (inside DayChanged dispatch)");
                return true;
            }

            bool isDayChanged = eventType == s_DayChangedType;
            if (isDayChanged)
                m_InDayChangedDispatch = true;

            if (m_PublishingStack.Count == 0)
                m_DispatchCancelled = false;

            // Cycle detection: if this event type is already being published, we have a cycle
            if (!m_PublishingStack.Add(eventType))
            {
                Log.Error($"CYCLE DETECTED: {eventType.Name} published while already being handled. Stack: {string.Join(" → ", m_PublishingStack.Select(t => t.Name))}");
                Utils.DiagnosticTracker.IncrementError("EventBus.Cycle");
                // Do NOT reset m_InDayChangedDispatch here — outer dispatch is still running.
                // The finally block handles cleanup when the outer dispatch completes.
                return false;
            }

            SubscriberEntry[] entries = Array.Empty<SubscriberEntry>();
            int handlerCount = 0;
            bool hasSubscribers = false;
            lock (m_Lock)
            {
                if (m_Subscribers.TryGetValue(eventType, out var set))
                {
                    hasSubscribers = true;
                    handlerCount = set.Count;
                    // Copy to pooled array to avoid allocation per publish
                    entries = ArrayPool<SubscriberEntry>.Shared.Rent(handlerCount);
                    for (int i = 0; i < handlerCount; i++)
                        entries[i] = set[i];

                    #if DEBUG
                    // Move history manipulation INSIDE lock to prevent race condition
                    if (m_History.Count >= MAX_HISTORY)
                        m_History.RemoveAt(0);  // O(n) but acceptable for small debug history
                    m_History.Add(new EventRecord(eventType.Name, DateTime.Now, handlerCount));
                    #endif
                }
            }

            if (!hasSubscribers)
            {
                m_PublishingStack.Remove(eventType);
                if (isDayChanged) DispatchDeferredActChanged();
                return false;
            }

            // Pre-compute sequence for ISequencedEvent (one boxing per Publish, not per handler)
            long sequence = long.MinValue;
            bool isSequenced = evt is ISequencedEvent;
            if (isSequenced)
                sequence = ((ISequencedEvent)evt).Sequence;

            try
            {
                #if DEBUG
                if (EnableLogging)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Publishing {typeof(T).Name} → {handlerCount} subscribers");
                }
                #endif

                // PERF: Get cached event name (avoids typeof(T).Name allocation per publish)
                if (!m_EventNameCache.TryGetValue(eventType, out var eventName))
                {
                    eventName = eventType.Name;
                    m_EventNameCache[eventType] = eventName;
                }

                bool accepted = false;
                for (int i = 0; i < handlerCount; i++)
                {
                    if (m_DispatchCancelled)
                        break;

                    var entry = entries[i];
                    var group = entry.BufferedGroup;

                    if (group != null)
                    {
                        if (group.IsDraining)
                        {
                            accepted |= EnqueueBufferedDelivery(group, entry, evt, eventType, isSequenced, sequence);
                            continue;
                        }

                        if (!group.HasBeenReady)
                        {
                            if (!group.IsReady())
                            {
                                accepted |= EnqueueBufferedDelivery(group, entry, evt, eventType, isSequenced, sequence);
                                continue;
                            }

                            DrainBufferedGroup(group);
                        }
                    }

                    accepted |= DispatchEntry(entry, evt, eventType, eventName, isSequenced, sequence);
                }

                return accepted;
            }
            finally
            {
                m_PublishingStack.Remove(eventType);
                ArrayPool<SubscriberEntry>.Shared.Return(entries, clearArray: true);

                // FIX S9-01: Dispatch deferred ActChanged AFTER DayChanged cleanup.
                // Cleanup (stack + pool) runs first → no resource leaks if deferred Publish throws.
                if (isDayChanged)
                {
                    DispatchDeferredActChanged();
                }
            }
        }

        public void DrainBuffered(string subscriberKey)
        {
            if (!IsMainThread())
            {
                Log.Error($"EventBus.DrainBuffered called from non-main thread (tid={System.Environment.CurrentManagedThreadId}, expected={m_MainThreadId})");
                Utils.DiagnosticTracker.IncrementError("EventBus.WrongThread");
                return;
            }

            if (string.IsNullOrWhiteSpace(subscriberKey))
                return;

            if (m_PublishingStack.Count == 0 && m_BufferedDrainDepth == 0)
                m_DispatchCancelled = false;

            BufferedSubscriberGroup? group;
            lock (m_Lock)
                m_BufferedGroups.TryGetValue(subscriberKey, out group);

            if (group == null)
                return;

            DrainBufferedGroup(group);
        }

        private void DrainBufferedGroup(BufferedSubscriberGroup group)
        {
            if (group.HasBeenReady && group.Pending.Count == 0)
                return;

            if (m_BufferedDrainDepth >= int.MaxValue)
                throw new InvalidOperationException("Buffered event drain depth overflow");

            group.HasBeenReady = true;
            group.IsDraining = true;
            m_BufferedDrainDepth++;
            try
            {
                while (group.Pending.Count > 0)
                {
                    if (m_DispatchCancelled)
                        break;

                    var pending = group.Pending.Dequeue();
                    DispatchBufferedPending(pending);
                }
            }
            finally
            {
                m_BufferedDrainDepth--;
                group.IsDraining = false;
            }
        }

        private bool EnqueueBufferedDelivery<T>(
            BufferedSubscriberGroup group,
            SubscriberEntry entry,
            T evt,
            Type eventType,
            bool isSequenced,
            long sequence)
            where T : IGameEvent
        {
            if (group.Pending.Count >= group.Capacity)
            {
                Log.Warn($"Buffered subscriber {group.SubscriberKey} overflow ({group.Capacity}), dropping {eventType.Name}");
                Utils.DiagnosticTracker.IncrementError($"EventBus.{eventType.Name}.BufferedOverflow");
                return false;
            }

            group.Pending.Enqueue(new BufferedPendingDelivery
            {
                ArrivalOrdinal = ++m_NextBufferedArrivalOrdinal,
                Entry = entry,
                Event = evt,
                EventType = eventType,
                IsSequenced = isSequenced,
                Sequence = sequence
            });
            return true;
        }

        private void DispatchBufferedPending(BufferedPendingDelivery pending)
        {
            var eventName = GetEventName(pending.EventType);
            DispatchCore(
                pending.Entry,
                pending.Event,
                pending.EventType,
                eventName,
                pending.IsSequenced,
                pending.Sequence,
                evt => pending.Entry.TypedDispatcher!(evt));
        }

        private bool DispatchEntry<T>(
            SubscriberEntry entry,
            T evt,
            Type eventType,
            string eventName,
            bool isSequenced,
            long sequence)
            where T : IGameEvent
        {
            return DispatchCore(
                entry,
                evt,
                eventType,
                eventName,
                isSequenced,
                sequence,
                delivery => ((Action<T>)entry.Handler)((T)delivery));
        }

        private bool DispatchCore(
            SubscriberEntry entry,
            IGameEvent evt,
            Type eventType,
            string eventName,
            bool isSequenced,
            long sequence,
            Action<IGameEvent> dispatcher)
        {
            if (isSequenced && entry.WatermarkKey != null
                && m_Watermarks.TryGetValue(entry.WatermarkKey, out long lastSeq)
                && sequence <= lastSeq)
            {
                return false;
            }

            var handler = entry.Handler;
            var subscriberType = handler.Target?.GetType();
            var methodName = handler.Method.Name;
            var cacheKey = (eventType, subscriberType ?? typeof(void), methodName);

            if (!m_ProfilerNameCache.TryGetValue(cacheKey, out var profilerName))
            {
                var subscriberName = subscriberType?.Name ?? "static";
                profilerName = $"Event.{eventName}.{subscriberName}.{methodName}";
                m_ProfilerNameCache[cacheKey] = profilerName;
            }

            bool handled = false;
            try
            {
                #if DEBUG
                if (EnableLogging)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"  → {profilerName}");
                }
                #endif

                using (PerformanceProfiler.Measure(profilerName))
                {
                    dispatcher(evt);
                }
                handled = true;
            }
            catch (Exception e)
            {
                Log.Error($"Handler failed for {eventName}: {e}");
                Utils.DiagnosticTracker.IncrementError($"EventBus.{eventName}");
            }

            if (handled && isSequenced && entry.WatermarkKey != null)
            {
                m_Watermarks[entry.WatermarkKey] = sequence;
            }

            return handled;
        }

        private string GetEventName(Type eventType)
        {
            if (!m_EventNameCache.TryGetValue(eventType, out var eventName))
            {
                eventName = eventType.Name;
                m_EventNameCache[eventType] = eventName;
            }

            return eventName;
        }

        private void RemoveEmptyBufferedGroups()
        {
            lock (m_Lock)
            {
                if (m_BufferedGroups.Count == 0)
                    return;

                var referenced = new HashSet<BufferedSubscriberGroup>();
                foreach (var set in m_Subscribers.Values)
                {
                    foreach (var entry in set)
                    {
                        if (entry.BufferedGroup != null)
                            referenced.Add(entry.BufferedGroup);
                    }
                }

                var removeKeys = new List<string>();
                foreach (var pair in m_BufferedGroups)
                {
                    if (!referenced.Contains(pair.Value) && pair.Value.Pending.Count == 0)
                        removeKeys.Add(pair.Key);
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    m_BufferedGroups.Remove(removeKeys[i]);
            }
        }

        private void DispatchDeferredActChanged()
        {
            m_InDayChangedDispatch = false;
            if (m_DeferredActChanged.Count == 0 && m_DeferredHeritageGranted.Count == 0)
                return;

            // Copy to temp to avoid modification during iteration.
            var pending = new List<ActChangedEvent>(m_DeferredActChanged);
            m_DeferredActChanged.Clear();
            foreach (var deferred in pending)
            {
                try
                {
                    Publish(deferred);
                }
                catch (Exception e)
                {
                    Log.Error($"Deferred ActChanged publish failed: {e}");
                    Utils.DiagnosticTracker.IncrementError("EventBus.DeferredActChanged");
                }
            }

            var pendingHeritage = new List<HeritageGrantedEvent>(m_DeferredHeritageGranted);
            m_DeferredHeritageGranted.Clear();
            foreach (var deferred in pendingHeritage)
            {
                try
                {
                    Publish(deferred);
                }
                catch (Exception e)
                {
                    Log.Error($"Deferred HeritageGranted publish failed: {e}");
                    Utils.DiagnosticTracker.IncrementError("EventBus.DeferredHeritageGranted");
                }
            }
        }

        /// <summary>
        /// Reset ISequencedEvent watermarks. Call on game load — watermarks are in-memory
        /// and retain stale sequence numbers from the previous session. Without reset,
        /// loading an earlier save suppresses all DayChangedEvents up to the old watermark.
        /// </summary>
        public void ResetWatermarks()
        {
            m_Watermarks.Clear();
        }

        public void Clear()
        {
            lock (m_Lock)
            {
                m_Subscribers.Clear();
                m_BufferedGroups.Clear();
                #if DEBUG
                m_History.Clear();
                #endif
            }
            // LOW-4 FIX: Clear caches to prevent monotonic growth across sessions
            m_EventNameCache.Clear();
            m_ProfilerNameCache.Clear();
            // Main-thread-only state — no lock needed
            bool wasDispatching = m_PublishingStack.Count > 0 || m_BufferedDrainDepth > 0;
            m_DispatchCancelled = wasDispatching;
            m_InDayChangedDispatch = false;
            m_DeferredActChanged.Clear();
            m_DeferredHeritageGranted.Clear();
            m_Watermarks.Clear();
            m_NextBufferedArrivalOrdinal = 0;
            // S27-M2 FIX: Clear publishing stack to prevent false "CYCLE DETECTED" after mid-dispatch Clear
            m_PublishingStack.Clear();
        }

        private bool IsMainThread()
            => System.Environment.CurrentManagedThreadId == m_MainThreadId;

        /// <summary>
        /// Get total subscriber count across all event types (for diagnostics).
        /// </summary>
        public int GetTotalSubscriberCount()
        {
            lock (m_Lock)
            {
                int total = 0;
                foreach (var set in m_Subscribers.Values)
                {
                    total += set.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// Get event type count (for diagnostics).
        /// </summary>
        public int GetEventTypeCount()
        {
            lock (m_Lock)
            {
                return m_Subscribers.Count;
            }
        }

        #if DEBUG
        /// <summary>Get subscriber count per event type (for debug UI)</summary>
        public Dictionary<string, int> GetSubscriberCounts()
        {
            lock (m_Lock)
            {
                return m_Subscribers.ToDictionary(
                    kvp => kvp.Key.FullName ?? kvp.Key.Name,
                    kvp => kvp.Value.Count
                );
            }
        }

        /// <summary>
        /// Validate that concrete IGameEvent types in the supplied assembly have subscribers.
        /// DEBUG-only diagnostic; production code should not depend on reflection catalogs.
        /// </summary>
        public void ValidateSubscriptions(Assembly eventAssembly)
        {
            if (eventAssembly == null)
                return;

            var subscriberCounts = GetSubscriberCounts();
            Type[] assemblyTypes;
            try
            {
                assemblyTypes = eventAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                assemblyTypes = e.Types.Where(t => t != null).ToArray();
                Log.Warn($"Subscription validation used partial type list for {eventAssembly.GetName().Name}: {e}");
            }

            var allEventTypes = assemblyTypes
                .Where(t => typeof(IGameEvent).IsAssignableFrom(t)
                            && !t.IsInterface
                            && !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToList();

            int orphaned = 0;
            foreach (var type in allEventTypes)
            {
                if (!subscriberCounts.ContainsKey(type.FullName ?? type.Name))
                {
                    Log.Warn($"No subscribers: {type.FullName}");
                    orphaned++;
                }
            }

            if (orphaned == 0)
                Log.Info($"All {allEventTypes.Count} event types have subscribers");
            else
                Log.Warn($"{orphaned}/{allEventTypes.Count} event types have no subscribers");
        }
        #endif
    }

    /// <summary>
    /// Internal subscriber entry with optional watermark key for ISequencedEvent dedup.
    /// </summary>
    internal sealed class SubscriberEntry
    {
        public Delegate Handler = null!;
        public int Priority;

        /// <summary>
        /// Null for non-sequenced events. "EventType:SubscriberType" for ISequencedEvent subscribers.
        /// Used as key in EventBus.m_Watermarks dictionary.
        /// </summary>
        public string? WatermarkKey;

        public BufferedSubscriberGroup? BufferedGroup;
        public Action<IGameEvent>? TypedDispatcher;
    }

    internal sealed class BufferedSubscriberGroup
    {
        public string SubscriberKey = null!;
        public Func<bool> IsReady = null!;
        public int Capacity;
        public bool HasBeenReady;
        public bool IsDraining;
        public BufferedPendingDeliveryQueue Pending = new();
        #if DEBUG
        public bool CapacityMismatchWarned;
        #endif
    }

    internal sealed class BufferedPendingDelivery
    {
        public long ArrivalOrdinal;
        public Type EventType = null!;
        public SubscriberEntry Entry = null!;
        public IGameEvent Event = null!;
        public bool IsSequenced;
        public long Sequence;
    }

    internal sealed class BufferedPendingDeliveryQueue
    {
        private readonly List<BufferedPendingDelivery> m_Items;

        public BufferedPendingDeliveryQueue() => m_Items = new List<BufferedPendingDelivery>();

        public BufferedPendingDeliveryQueue(int capacity) => m_Items = new List<BufferedPendingDelivery>(capacity);

        public int Count => m_Items.Count;

        public void Enqueue(BufferedPendingDelivery item) => m_Items.Add(item);

        public BufferedPendingDelivery Dequeue()
        {
            var item = m_Items[0];
            m_Items.RemoveAt(0);
            return item;
        }
    }

    #if DEBUG
    public record EventRecord(string EventType, DateTime Timestamp, int SubscriberCount);
    #endif
}
