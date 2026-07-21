using System;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Interfaces.Core
{
#pragma warning disable CA1040 // Empty interface - marker interface for type safety in EventBus
    public interface IGameEvent { }
#pragma warning restore CA1040

    /// <summary>
    /// Event bus for decoupled communication between systems.
    ///
    /// THREADING MODEL: Main-thread only.
    /// ✅ Safe: SystemBase.OnUpdate, UISystemBase, MonoBehaviour, React triggers
    /// ❌ NOT safe: IJobChunk, IJobEntity, Burst code
    ///
    /// For events from Jobs: use NativeQueue + drain in OnUpdate.
    /// </summary>
    [InfrastructureService]
    public interface IEventBus
    {
        /// <summary>
        /// Subscribe to events of type T.
        /// Re-subscribing the same delegate is a no-op; call Unsubscribe first
        /// if the handler needs a different priority.
        /// </summary>
        /// <typeparam name="T">Event type implementing IGameEvent</typeparam>
        /// <param name="handler">Callback invoked when event is published</param>
        void Subscribe<T>(Action<T> handler) where T : IGameEvent;

        /// <summary>
        /// Subscribe to events of type T with explicit dispatch priority.
        /// Lower priority number = dispatched first.
        /// Re-subscribing the same delegate is a no-op; priority is not changed
        /// by a second Subscribe call.
        /// </summary>
        /// <typeparam name="T">Event type implementing IGameEvent</typeparam>
        /// <param name="handler">Callback invoked when event is published</param>
        /// <param name="priority">Dispatch order (lower = first). Default is 100.</param>
        void Subscribe<T>(Action<T> handler, int priority) where T : IGameEvent;

        /// <summary>
        /// Subscribe to events of type T while deferring handler invocation until
        /// <paramref name="isReady"/> returns true or <see cref="DrainBuffered"/>
        /// is called for <paramref name="subscriberKey"/>.
        /// </summary>
        /// <remarks>
        /// Main-thread only, same as <see cref="Publish{T}"/>. Pending deliveries
        /// are FIFO across all buffered event types for the same subscriber key.
        /// ISequencedEvent watermarks advance only after the real handler succeeds.
        /// </remarks>
        /// <typeparam name="T">Event type implementing IGameEvent</typeparam>
        /// <param name="handler">Callback invoked when event is delivered</param>
        /// <param name="priority">Dispatch order (lower = first)</param>
        /// <param name="subscriberKey">Stable subscriber identity for buffering and sequenced watermarks</param>
        /// <param name="isReady">Readiness predicate checked before direct dispatch</param>
        /// <param name="capacity">Maximum pending deliveries for this subscriber key</param>
        void SubscribeBuffered<T>(
            Action<T> handler,
            int priority,
            string subscriberKey,
            Func<bool> isReady,
            int capacity = 1024)
            where T : IGameEvent;

        /// <summary>
        /// Mark a buffered subscriber key ready and deliver queued events in
        /// arrival order. Idempotent when no matching buffered group exists.
        /// </summary>
        /// <param name="subscriberKey">Stable subscriber identity passed to SubscribeBuffered</param>
        void DrainBuffered(string subscriberKey);

        /// <summary>
        /// Unsubscribe from events of type T.
        /// </summary>
        /// <typeparam name="T">Event type implementing IGameEvent</typeparam>
        /// <param name="handler">Previously subscribed callback</param>
        void Unsubscribe<T>(Action<T> handler) where T : IGameEvent;

        /// <summary>
        /// Publish event to all subscribers. Main-thread only; background callers must marshal
        /// to a system update or queue and drain on the main thread.
        /// </summary>
        /// <typeparam name="T">Event type implementing IGameEvent</typeparam>
        /// <param name="evt">Event instance to publish</param>
        bool Publish<T>(T evt) where T : IGameEvent;

        /// <summary>
        /// Clear all subscribers. Call on cleanup/dispose.
        /// </summary>
        void Clear();

        /// <summary>
        /// Reset sequence watermarks for ISequencedEvent types.
        /// Must be called after save/load to prevent stale watermarks from suppressing events.
        /// </summary>
        void ResetWatermarks();
    }
}
