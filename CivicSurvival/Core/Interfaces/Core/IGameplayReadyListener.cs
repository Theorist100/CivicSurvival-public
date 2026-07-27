namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Implement on any <see cref="CivicSurvival.Core.Systems.Base.CivicSystemBase"/> or
    /// <see cref="CivicSurvival.Core.Systems.Base.CivicUISystemBase"/> subclass that needs a
    /// callback at the precise moment the gameplay session becomes ready — after the PLVS
    /// post-load pass finishes validators, singleton restore, building-ref rebind, and
    /// initializers.
    ///
    /// Use this instead of <c>OnStartRunning</c> for any "city loaded" work. The ECS
    /// <c>OnStartRunning</c> hook fires on the first frame the system is eligible to update,
    /// which for UI systems (no <c>RequireForUpdate</c>) is during cold-boot menu — far
    /// before any city exists.
    ///
    /// Registration is automatic: <c>CivicSystemBase.OnCreate()</c> /
    /// <c>CivicUISystemBase.OnCreate()</c> detect this interface and subscribe to
    /// <see cref="CivicSurvival.Core.Services.CivicGameLifecycle.GameplayReady"/>. The
    /// symmetric <c>OnDestroy</c> path unsubscribes. No manual <c>+=</c>/<c>-=</c> wiring.
    ///
    /// Multicast invocation order = subscribe order = registration order. Exception
    /// isolation is provided by <c>CivicGameLifecycle.InvokeIsolated</c>: a throwing
    /// listener is logged and does not block other listeners or propagate out of the
    /// PLVS post-load <c>finally</c> block. The same isolation is used by the
    /// late-created replay path, so hot-reload listeners do not throw out of
    /// <c>OnUpdate</c>.
    ///
    /// Late-created listeners are handled by the base-class deferred replay path:
    /// if <c>IsGameplayReady</c> is already true at subscription time, the base class
    /// invokes <c>OnGameplayReady</c> on the first <c>OnUpdate</c> after creation (not
    /// inside <c>OnCreate</c>, where subclass setup may still be in flight).
    /// This replay is intentionally tied to update eligibility: a disabled system or
    /// a system whose <c>RequireForUpdate</c> query is not satisfied receives it only
    /// when Unity next runs its <c>OnUpdate</c>.
    ///
    /// Fires on every gameplay load — first load, second load, etc. Listeners that need
    /// "only first time" semantics track that themselves (boolean flag pattern).
    ///
    /// Typical usage:
    /// <code>
    /// public partial class GameSessionUISystem : CivicUISystemBase, IGameplayReadyListener
    /// {
    ///     private bool m_InitialNotificationsSent;
    ///
    ///     public void OnGameplayReady()
    ///     {
    ///         if (m_InitialNotificationsSent) return;
    ///         SendInitialNotifications();
    ///         m_InitialNotificationsSent = true;
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IGameplayReadyListener
    {
        /// <summary>
        /// Called from <c>PostLoadValidationSystem</c> post-load <c>finally</c> block,
        /// after validators / singleton restore / building-ref rebind / initializers
        /// complete. All gameplay state is hydrated; safe to read singletons and
        /// publish UI data. Also delivered via deferred replay (first <c>OnUpdate</c>
        /// after creation) when the listener is created late, while
        /// <c>CivicGameLifecycle.IsGameplayReady</c> is already true. Deferred replay
        /// uses the same exception isolation as the normal lifecycle event.
        /// </summary>
        void OnGameplayReady();
    }
}
