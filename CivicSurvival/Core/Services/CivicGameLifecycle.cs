using System;
using Game;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Process-local lifecycle oracle for systems that persist across game/menu transitions.
    /// Vanilla does not destroy every mod system on exit to menu, so systems use this
    /// read-only flag to stay dormant outside a loaded gameplay session.
    /// </summary>
    public static class CivicGameLifecycle
    {
        private static volatile bool s_IsLoadingComplete;
        private static volatile bool s_IsGameplayReady;
        private static bool s_DefaultSubscribersRegistered;

        /// <summary>
        /// Diagnostic milestone: vanilla deserialize/run-once finished, before the
        /// post-load validation pass. Intentionally NOT a gate input — nothing in
        /// gameplay code reads this (verified: zero code consumers since the
        /// original design, which classified it as a "diagnostic/low-risk signal,
        /// not the main trigger"). Gating is a single readiness question answered
        /// by <see cref="IsGameplayReady"/>; this exists for the
        /// <c>[Lifecycle] LoadingComplete</c> log line and hot-reload symmetry.
        /// Do not wire gameplay logic to it — use <see cref="IsGameplayReady"/>.
        /// </summary>
        public static bool IsLoadingComplete => s_IsLoadingComplete;

        /// <summary>
        /// The readiness gate. True only after PostLoadValidationSystem completes
        /// its post-load pass and flips it in its OnUpdate finally. This is the one
        /// flag every gameplay gate reads (CivicSystemBase / CivicUISystemBase
        /// OnUpdate, TriggerRegistry.IsGateOpen, GameplayOnly).
        /// </summary>
        public static bool IsGameplayReady => s_IsGameplayReady;

#pragma warning disable CA1003, CIVIC080 // Process-lifetime lifecycle bus; subscribers must own unsubscribe until Phase B event args land.
        /// <summary>
        /// Diagnostic-only counterpart to <see cref="IsLoadingComplete"/>. Has no
        /// code subscribers by design (gameplay setup subscribes to
        /// <see cref="GameplayReady"/> instead). Kept as a deliberate extension
        /// point + log marker, not dead code — see <see cref="IsLoadingComplete"/>.
        /// </summary>
        public static event Action? LoadingComplete;
        public static event Action? GameplayReady;
        public static event Action? GameplayEnded;
#pragma warning restore CA1003, CIVIC080

        /// <summary>
        /// Records the diagnostic "vanilla load finished" milestone. This does NOT
        /// open the gameplay gate (that is <see cref="MarkGameplayReady"/>, owned by
        /// PostLoadValidationSystem). Its observable effect is the
        /// <c>[Lifecycle] LoadingComplete</c> log line — the flag and event it sets
        /// have no gameplay consumer by design (see <see cref="IsLoadingComplete"/>).
        /// </summary>
        internal static void MarkLoadingComplete(Purpose purpose, GameMode mode)
        {
            if (s_IsLoadingComplete)
                return;

            s_IsLoadingComplete = true;
            // Diagnostic marker only — not the gate flip. See IsLoadingComplete.
            Mod.Log.Info($"[Lifecycle] LoadingComplete (diagnostic) mode={mode} purpose={purpose}");
            InvokeIsolated(LoadingComplete, nameof(LoadingComplete));
        }

        internal static void MarkGameplayReady(Purpose purpose, GameMode mode)
        {
            if (s_IsGameplayReady)
                return;

            s_IsGameplayReady = true;
            Mod.Log.Info($"[Lifecycle] GameplayReady mode={mode} purpose={purpose}");
            InvokeIsolated(GameplayReady, nameof(GameplayReady));
        }

        internal static void SnapHotReloadReady(GameMode mode)
        {
            RegisterDefaultSubscribers();

            bool fireLoadingComplete = !s_IsLoadingComplete;
            bool fireGameplayReady = !s_IsGameplayReady;

            s_IsLoadingComplete = true;
            s_IsGameplayReady = true;

            Mod.Log.Info($"[Lifecycle] HotReloadRecovery: gameMode={mode} already loaded");

            if (fireLoadingComplete)
                InvokeIsolated(LoadingComplete, nameof(LoadingComplete));
            if (fireGameplayReady)
                InvokeIsolated(GameplayReady, nameof(GameplayReady));
        }

        internal static void MarkNotReady(string reason)
        {
            bool wasActive = s_IsLoadingComplete || s_IsGameplayReady;
            s_IsGameplayReady = false;
            s_IsLoadingComplete = false;

            if (!wasActive)
                return;

            Mod.Log.Info($"[Lifecycle] GameplayEnded ({reason})");
            InvokeIsolated(GameplayEnded, nameof(GameplayEnded));
        }

        /// <summary>
        /// Re-closes the gameplay gate at the start of a gameplay load. A warm
        /// reload (loading a save over a running city) goes straight
        /// Game/LoadGame with no MainMenu/Cleanup pass (decompile
        /// GameManager.cs:1206-1208, 1022-1104), so MarkNotReady never runs and
        /// IsGameplayReady would leak true from the prior session — letting every
        /// CivicSystemBase consumer tick through the deserialize→PostLoadValidation
        /// window. Clearing the flags re-closes the gate until
        /// PostLoadValidationSystem re-flips ready in its OnUpdate finally after
        /// RestoreSingletonOwners. Unlike MarkNotReady this does NOT raise
        /// GameplayEnded: the session is not ending, the city is not unloaded.
        /// </summary>
        internal static void MarkReloadPending()
        {
            bool wasReady = s_IsGameplayReady;
            s_IsGameplayReady = false;
            s_IsLoadingComplete = false;
            if (wasReady)
                Mod.Log.Info("[Lifecycle] ReloadPending: gameplay gate re-closed for reload");
        }

        internal static void RegisterDefaultSubscribers()
        {
            if (s_DefaultSubscribersRegistered)
                return;

            GameplayReady += OnPerfInit;
            GameplayEnded += OnPerfShutdown;
            s_DefaultSubscribersRegistered = true;
        }

        internal static void UnregisterDefaultSubscribers()
        {
            if (!s_DefaultSubscribersRegistered)
                return;

            GameplayReady -= OnPerfInit;
            GameplayEnded -= OnPerfShutdown;
            s_DefaultSubscribersRegistered = false;
        }

        internal static void ReplayGameplayReady(IGameplayReadyListener listener)
        {
            if (!s_IsGameplayReady)
                return;

            InvokeIsolated(listener.OnGameplayReady, nameof(GameplayReady));
        }

        private static void InvokeIsolated(Action? evt, string eventName)
        {
            if (evt == null)
                return;

            foreach (var subscriber in evt.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[Lifecycle] {eventName} subscriber failed: {ex}");
                }
            }
        }

        private static void OnPerfInit()
            => PerformanceProfiler.Initialize(ModPaths.LogsDirectory);

        private static void OnPerfShutdown()
            => PerformanceProfiler.Shutdown();

        /// <summary>
        /// Wraps a trigger / UI callback so it silently no-ops while the gameplay
        /// session is not ready. Use for raw <c>TriggerBinding</c> registrations
        /// whose handlers would mutate persisted save state, modal flags, or
        /// gameplay-only services if invoked from cold-boot menu via React.
        ///
        /// Triggers registered through <c>TriggerRegistry.Add(name, gate, ...)</c>
        /// already pick up the same lifecycle gate via <c>IsGateOpen</c>; this
        /// helper covers the raw-binding path.
        /// </summary>
        public static Action GameplayOnly(Action handler) =>
            () => { if (s_IsGameplayReady) handler(); };

        public static Action<T> GameplayOnly<T>(Action<T> handler) =>
            arg => { if (s_IsGameplayReady) handler(arg); };

        public static Action<T1, T2> GameplayOnly<T1, T2>(Action<T1, T2> handler) =>
            (a1, a2) => { if (s_IsGameplayReady) handler(a1, a2); };
    }
}
