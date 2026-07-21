using System;
using Game;
using Game.UI;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for CivicSurvival UI systems with automatic profiling.
    ///
    /// Benefits:
    /// - EventBus: Lazy-cached, race-condition safe
    /// - Auto-profiling: All UI systems tracked in PERF.log
    /// - UI bindings support from UISystemBase
    ///
    /// Usage:
    /// public partial class MyUISystem : CivicUISystemBase
    /// {
    ///     protected override void OnUpdateImpl()
    ///     {
    ///         // your code - automatically profiled
    ///     }
    /// }
    ///
    /// Pattern mirrored in CivicSystemBase.cs
    /// </summary>
    public abstract partial class CivicUISystemBase : UISystemBase
    {
        private IEventBus? m_EventBusCache;
        private string? m_ProfileName;
        // Late-created listeners miss GameplayReady that already fired. Deferred replay
        // on first OnUpdate; do not call OnGameplayReady synchronously from base OnCreate
        // because subclass OnCreate has not finished wiring bindings/triggers yet.
        // Ephemeral process-lifetime signal — never persisted; CIVIC150 false positive.
#pragma warning disable CIVIC150 // Ephemeral process-lifetime replay flag; intentionally not persisted
        private bool m_ReplayGameplayReadyAfterCreate;
#pragma warning restore CIVIC150

        /// <summary>
        /// Lazy-cached EventBus. Safe to use in OnCreate, OnUpdate, OnDestroy.
        /// Returns null only if EventBus not yet registered (early startup).
        /// </summary>
        // FIX M7: Cache after first successful resolve — avoids lock on every access
        protected IEventBus? EventBus =>
            m_EventBusCache ??= ServiceRegistry.TryGet<IEventBus>();

        protected override void OnCreate()
        {
            base.OnCreate();

            // (CivicUISystemBase descends from a UI base, so `this` can never be
            // PostLoadValidationSystem — the former self-guard was dead code per
            // CS0184. PLVS handles its own registration through CivicSystemBase.)
            var plvs = World.GetOrCreateSystemManaged<PostLoadValidationSystem>();
#pragma warning disable S3060 // Intentional: base class auto-registration pattern mirrors CivicSystemBase
            if (this is IInitializable init)
                plvs?.RegisterInitializable(init);
            if (this is IPostLoadValidation validator)
                plvs?.Register(validator);
            if (this is ICivicSingletonOwner singletonOwner)
                plvs?.RegisterSingletonOwner(singletonOwner);
            if (this is IBuildingRefRebindOwner buildingRefRebindOwner)
                plvs?.RegisterBuildingRefRebindOwner(buildingRefRebindOwner);
            if (this is IResettable resettable)
                plvs?.RegisterResettable(resettable);
            if (this is IGameplayReadyListener readyListener)
                CivicGameLifecycle.GameplayReady += readyListener.OnGameplayReady;
            if (this is IGameplayEndedListener endedListener)
                CivicGameLifecycle.GameplayEnded += endedListener.OnGameplayEnded;
            if (this is IGameplayReadyListener && CivicGameLifecycle.IsGameplayReady)
                m_ReplayGameplayReadyAfterCreate = true;
#pragma warning restore S3060
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            FlushPendingRequiredEventSubscriptions();
        }

#if DEBUG
        /// <summary>
        /// DEBUG "Toggle" panel command: enable or disable this UI system's scheduler
        /// tick. The <c>Enabled</c> assignment stays on the owning system because
        /// CIVIC481 bans cross-system <c>Enabled</c> pokes and its message points to a
        /// command as the sanctioned route. Called only by <c>DomainToggleRegistry</c>
        /// so a domain's UI panel disables together with its gameplay systems.
        /// </summary>
        internal void SetSchedulerEnabled(bool on)
        {
            Enabled = on;
        }
#endif

        /// <summary>
        /// Unsubscribe from an event safely during OnDestroy.
        /// If EventBus is null (system was disabled or never subscribed), silently skips.
        /// </summary>
        protected void UnsubscribeSafe<T>(Action<T> handler) where T : IGameEvent
        {
#pragma warning disable CIVIC139 // Intentional: null means nothing was subscribed
            EventBus?.Unsubscribe(handler);
#pragma warning restore CIVIC139
        }

        protected override void OnDestroy()
        {
            ClearPendingRequiredEventSubscriptions();

            var plvs = World.GetExistingSystemManaged<PostLoadValidationSystem>();
#pragma warning disable S3060 // Intentional: base class symmetric lifecycle wiring
            if (this is IPostLoadValidation validator)
                plvs?.Unregister(validator);
            if (this is IInitializable init)
                plvs?.UnregisterInitializable(init);
            if (this is ICivicSingletonOwner singletonOwner)
                plvs?.UnregisterSingletonOwner(singletonOwner);
            if (this is IBuildingRefRebindOwner buildingRefRebindOwner)
                plvs?.UnregisterBuildingRefRebindOwner(buildingRefRebindOwner);
            if (this is IResettable resettable)
                plvs?.UnregisterResettable(resettable);
            if (this is IGameplayReadyListener readyListener)
                CivicGameLifecycle.GameplayReady -= readyListener.OnGameplayReady;
            if (this is IGameplayEndedListener endedListener)
                CivicGameLifecycle.GameplayEnded -= endedListener.OnGameplayEnded;
#pragma warning restore S3060

            base.OnDestroy();
        }

        /// <summary>
        /// Name used in profiler. Override to customize.
        /// Default: ClassName.OnUpdate
        /// </summary>
        protected virtual string ProfileName =>
            m_ProfileName ??= $"{GetType().Name}.OnUpdate";

        /// <summary>
        /// Most Civic UI systems expose city/session data and should sleep in menu.
        /// Override to false only for menu-safe shell/settings/modal/toast bindings.
        /// </summary>
        protected virtual bool RequiresLoadedGame => true;

        /// <summary>
        /// Let vanilla disable city/gameplay UI systems in menu, while keeping the
        /// explicit menu-safe whitelist alive through <see cref="RequiresLoadedGame"/>.
        /// The base lifecycle gate remains the authoritative safety layer.
        /// </summary>
        public override GameMode gameMode => RequiresLoadedGame ? GameMode.Game : GameMode.All;

        /// <summary>
        /// SEALED - do not override. Wraps OnUpdateImpl with profiling.
        /// </summary>
        protected sealed override void OnUpdate()
        {
            FlushPendingRequiredEventSubscriptions();
            ReplayGameplayReadyIfNeeded();

            if (RequiresLoadedGame && !CivicGameLifecycle.IsGameplayReady)
                return;

            // Per-system profiling, gated by PerformanceProfiler.Enabled (its own
            // switch, not the log level) so it can record at Level.Info where sync
            // numbers are honest. Enabled=false → no-op, zero cost (beta state).
            using (PerformanceProfiler.Measure(ProfileName))
            {
                OnUpdateImpl();
                // UISystemBase owns IUpdateBinding pumping for GetterValueBinding.
                // Throttled UI subclasses still need this every frame.
                base.OnUpdate();
            }
        }

        // Delivers the GameplayReady callback to listeners created after the event
        // already fired (hot-reload-in-game, future lazy-registration). Runs before
        // the gate so the callback lands ahead of the first OnUpdateImpl on the same
        // frame. Re-checks IsGameplayReady to suppress stale callback if exit-to-menu
        // happened between OnCreate and first OnUpdate.
        private void ReplayGameplayReadyIfNeeded()
        {
            if (!m_ReplayGameplayReadyAfterCreate)
                return;

            m_ReplayGameplayReadyAfterCreate = false;

#pragma warning disable S3060 // Intentional: pattern match drives the replay; listener type is established at OnCreate time
            if (this is IGameplayReadyListener readyListener && CivicGameLifecycle.IsGameplayReady)
                CivicGameLifecycle.ReplayGameplayReady(readyListener);
#pragma warning restore S3060
        }

        /// <summary>
        /// Override this instead of OnUpdate. Automatically profiled.
        /// </summary>
#pragma warning disable CA1711 // "Impl" suffix is intentional - matches sealed/virtual template pattern
        protected abstract void OnUpdateImpl();
#pragma warning restore CA1711

    }
}
