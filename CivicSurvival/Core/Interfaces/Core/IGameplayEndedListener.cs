namespace CivicSurvival.Core.Interfaces.Core
{
    /// <summary>
    /// Implement on any <see cref="CivicSurvival.Core.Systems.Base.CivicSystemBase"/> or
    /// <see cref="CivicSurvival.Core.Systems.Base.CivicUISystemBase"/> subclass that needs a
    /// callback when the gameplay session ends (Exit-to-Main-Menu).
    ///
    /// Fires from <see cref="CivicSurvival.Core.Services.CivicGameLifecycle.MarkNotReady"/>,
    /// invoked from <c>CivicGameLifecycleSystem.OnGameLoadingComplete(Cleanup, MainMenu)</c>.
    /// At this point vanilla has finished its cleanup deserialization
    /// (<c>DeserializationSystem.RunOnce(Cleanup)</c>); the gate is now closed and gameplay
    /// systems are dormant for the rest of the menu session.
    ///
    /// Use this for cleanup work that the dormant gate alone can't cover: clearing static
    /// caches, releasing UI bindings, resetting menu-visible state. Do not destroy entities
    /// from here — vanilla cleanup already ran.
    ///
    /// Registration is automatic via <c>CivicSystemBase.OnCreate</c> /
    /// <c>CivicUISystemBase.OnCreate</c>. Unsubscribe is symmetric in <c>OnDestroy</c>.
    /// Exception isolation per listener is provided by <c>CivicGameLifecycle.InvokeIsolated</c>.
    /// </summary>
    public interface IGameplayEndedListener
    {
        /// <summary>
        /// Called when the gameplay session ends (Exit-to-Main-Menu). Vanilla cleanup
        /// deserialization has completed; the gate is closed; gameplay systems will not
        /// tick until the next <c>GameplayReady</c>.
        /// </summary>
        void OnGameplayEnded();
    }
}
