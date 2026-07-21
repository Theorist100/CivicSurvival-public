using System;
using System.Collections.Generic;
using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Systems.Base
{
    public abstract partial class CivicSystemBase
    {
        private List<Action<IEventBus>>? m_PendingRequiredEventSubscriptions;

        protected void SubscribeRequired<TEvent>(Action<TEvent> handler)
            where TEvent : IGameEvent
        {
            var bus = EventBus;
            if (bus == null)
            {
#pragma warning disable CIVIC044 // Subscribe is deferred through the same helper; subclasses still own UnsubscribeSafe
                QueueRequiredEventSubscription(b => b.Subscribe(handler));
#pragma warning restore CIVIC044
                return;
            }

#pragma warning disable CIVIC044 // Subscribe is delegated — each subclass manages its own Unsubscribe
            bus.Subscribe(handler);
#pragma warning restore CIVIC044
        }

        protected void SubscribeRequired<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : IGameEvent
        {
            var bus = EventBus;
            if (bus == null)
            {
#pragma warning disable CIVIC044 // Subscribe is deferred through the same helper; subclasses still own UnsubscribeSafe
                QueueRequiredEventSubscription(b => b.Subscribe(handler, priority));
#pragma warning restore CIVIC044
                return;
            }

#pragma warning disable CIVIC044 // Subscribe is delegated — each subclass manages its own Unsubscribe
            bus.Subscribe(handler, priority);
#pragma warning restore CIVIC044
        }

        private void QueueRequiredEventSubscription(Action<IEventBus> subscribe)
        {
            m_PendingRequiredEventSubscriptions ??= new List<Action<IEventBus>>(2);
            m_PendingRequiredEventSubscriptions.Add(subscribe);
        }

        private void FlushPendingRequiredEventSubscriptions()
        {
            if (m_PendingRequiredEventSubscriptions == null || m_PendingRequiredEventSubscriptions.Count == 0)
                return;

            var bus = EventBus;
            if (bus == null)
                throw new InvalidOperationException($"{GetType().Name}: EventBus unavailable for required subscription");

            for (int i = 0; i < m_PendingRequiredEventSubscriptions.Count; i++)
                m_PendingRequiredEventSubscriptions[i](bus);

            m_PendingRequiredEventSubscriptions.Clear();
        }

        private void ClearPendingRequiredEventSubscriptions()
        {
            if (m_PendingRequiredEventSubscriptions is { Count: > 0 })
                System.Diagnostics.Debug.Fail($"{GetType().Name}: required EventBus subscriptions were still pending at destroy");

            m_PendingRequiredEventSubscriptions?.Clear();
        }
    }
}
