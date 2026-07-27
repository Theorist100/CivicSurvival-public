using System.Runtime.CompilerServices;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Extension methods for IEventBus.
    /// Provides safe alternatives to null-conditional patterns.
    /// </summary>
    public static class EventBusExtensions
    {
        private static readonly LogContext Log = new("EventBusExtensions");

        /// <summary>
        /// Safely publish an event, logging warning if bus is null.
        /// Use this instead of <c>eventBus?.Publish(evt)</c> to avoid silent failures.
        /// </summary>
        /// <typeparam name="T">Event type</typeparam>
        /// <param name="eventBus">Event bus (may be null)</param>
        /// <param name="evt">Event to publish</param>
        /// <param name="caller">Caller name for logging (optional)</param>
        /// <returns>True if the bus accepted the event for delivery, false if it could not be delivered.</returns>
        public static bool SafePublish<T>(this IEventBus? eventBus, T evt, [CallerMemberName] string caller = "") where T : IGameEvent
        {
            if (eventBus == null)
            {
                var callerInfo = string.IsNullOrEmpty(caller) ? "" : $"[{caller}] ";
                Log.Warn($"{callerInfo}EventBus unavailable - {typeof(T).Name} not published");
                return false;
            }

            return eventBus.Publish(evt);
        }

        /// <summary>
        /// Safely publish an event without logging (for high-frequency events).
        /// Use when logging would spam the log file.
        /// </summary>
        public static bool SafePublishSilent<T>(this IEventBus? eventBus, T evt) where T : IGameEvent
        {
            if (eventBus == null)
                return false;

            return eventBus.Publish(evt);
        }
    }
}
