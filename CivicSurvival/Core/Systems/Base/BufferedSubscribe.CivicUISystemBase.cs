using System;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Systems.Base
{
    public abstract partial class CivicUISystemBase
    {
        private bool m_BufferedHandlersReady;

        protected void SubscribeBufferedUntilReady<TEvent>(Action<TEvent> handler)
            where TEvent : IGameEvent
            => SubscribeBufferedUntilReadyInternal(handler, priority: 100);

        protected void SubscribeBufferedUntilReady<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : IGameEvent
            => SubscribeBufferedUntilReadyInternal(handler, priority);

        private void SubscribeBufferedUntilReadyInternal<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : IGameEvent
        {
            var subscriberKey = GetType().FullName!;
            var bus = EventBus;
            if (bus == null)
            {
#pragma warning disable CIVIC044 // Subscribe is deferred through the same helper; subclasses still own UnsubscribeSafe
                QueueRequiredEventSubscription(b => b.SubscribeBuffered(
                    handler,
                    priority,
                    subscriberKey,
                    isReady: () => m_BufferedHandlersReady));
#pragma warning restore CIVIC044
                return;
            }

#pragma warning disable CIVIC044 // Subscribe is delegated; subclasses still own UnsubscribeSafe
            bus.SubscribeBuffered(
                handler,
                priority,
                subscriberKey,
                isReady: () => m_BufferedHandlersReady);
#pragma warning restore CIVIC044
        }

        protected void MarkEventHandlersReady()
        {
            FlushPendingRequiredEventSubscriptions();

            if (m_BufferedHandlersReady)
                return;

            m_BufferedHandlersReady = true;
            EventBus?.DrainBuffered(GetType().FullName!);
        }
    }
}
