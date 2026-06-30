using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces;
using Game;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for CivicSurvival systems with AUTOMATIC profiling.
    ///
    /// Benefits:
    /// - EventBus: Lazy-cached, race-condition safe
    /// - Auto-profiling: All systems tracked in PERF.log (no manual code needed)
    /// - No boilerplate in each system
    ///
    /// Usage:
    /// public partial class MySystem : CivicSystemBase
    /// {
    ///     protected override void OnUpdateImpl()
    ///     {
    ///         // your code - automatically profiled
    ///     }
    /// }
    /// </summary>
    public abstract partial class CivicSystemBase : GameSystemBase
    {
        private IEventBus? m_EventBusCache;
        private string? m_ProfileName;
        // Late-created listeners miss GameplayReady that already fired. We do not
        // synchronously invoke OnGameplayReady from base OnCreate because subclass
        // OnCreate has not finished wiring bindings/triggers yet. Defer to first
        // OnUpdate after creation, gated by the still-true IsGameplayReady.
        // Ephemeral process-lifetime signal: armed only in OnCreate, consumed on
        // first OnUpdate. Save/load fires GameplayReady through PLVS post-load;
        // no persisted state to lose — analyzer can't see that.
#pragma warning disable CIVIC150 // Ephemeral process-lifetime replay flag; intentionally not persisted (see comment above)
        private bool m_ReplayGameplayReadyAfterCreate;
#pragma warning restore CIVIC150

        protected override void OnCreate()
        {
            base.OnCreate();
#pragma warning disable S3060 // Intentional: PostLoadValidationSystem cannot register with itself
            if (this is PostLoadValidationSystem)
                return;
#pragma warning restore S3060

            var plvs = World.GetOrCreateSystemManaged<PostLoadValidationSystem>();
#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IInitializable, wiring is transparent
            if (this is IInitializable init)
#pragma warning restore S3060
                plvs?.RegisterInitializable(init);

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IPostLoadValidation, wiring is transparent
            if (this is IPostLoadValidation validator)
#pragma warning restore S3060
                plvs?.Register(validator);

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements ICivicSingletonOwner
            if (this is ICivicSingletonOwner singletonOwner)
#pragma warning restore S3060
                plvs?.RegisterSingletonOwner(singletonOwner);

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IBuildingRefRebindOwner
            if (this is IBuildingRefRebindOwner buildingRefRebindOwner)
#pragma warning restore S3060
                plvs?.RegisterBuildingRefRebindOwner(buildingRefRebindOwner);

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IResettable
            if (this is IResettable resettable)
#pragma warning restore S3060
                plvs?.RegisterResettable(resettable);

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IGameplayReadyListener
            if (this is IGameplayReadyListener readyListener)
#pragma warning restore S3060
                CivicGameLifecycle.GameplayReady += readyListener.OnGameplayReady;

#pragma warning disable S3060 // Intentional: base class auto-registration pattern — subclass implements IGameplayEndedListener
            if (this is IGameplayEndedListener endedListener)
#pragma warning restore S3060
                CivicGameLifecycle.GameplayEnded += endedListener.OnGameplayEnded;

#pragma warning disable S3060 // Intentional: arm deferred replay only when listener was created after GameplayReady already fired
            if (this is IGameplayReadyListener && CivicGameLifecycle.IsGameplayReady)
                m_ReplayGameplayReadyAfterCreate = true;
#pragma warning restore S3060
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            FlushPendingRequiredEventSubscriptions();
        }

        /// <summary>
        /// Lazy-cached EventBus. Safe to use in OnCreate, OnUpdate, OnDestroy.
        /// Returns null only if EventBus not yet registered (early startup).
        /// </summary>
        // FIX M7: Cache after first successful resolve — avoids lock on every access
        protected IEventBus? EventBus =>
            m_EventBusCache ??= ServiceRegistry.TryGet<IEventBus>();

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
        /// Most Civic gameplay systems should sleep outside a loaded city. Override
        /// to false only for menu-safe global systems and lifecycle infrastructure.
        /// </summary>
        protected virtual bool RequiresLoadedGame => true;

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
            }
        }

#if DEBUG
        /// <summary>
        /// DEBUG "Toggle" panel command: enable or disable this system's scheduler
        /// tick. The <c>Enabled</c> assignment stays on the owning system because
        /// CIVIC481 bans cross-system <c>Enabled</c> pokes and its message points to a
        /// command as the sanctioned route. Called only by <c>DomainToggleRegistry</c>
        /// (the DEBUG perf panel); no gameplay code path touches it.
        /// </summary>
        internal void SetSchedulerEnabled(bool on)
        {
            Enabled = on;
        }
#endif

        // Delivers the GameplayReady callback to listeners that were created after the
        // event already fired (hot-reload-in-game, future lazy-registration). Runs
        // before the gate so the callback lands ahead of the first OnUpdateImpl on the
        // same frame. Re-checks IsGameplayReady at replay time so a quick exit-to-menu
        // between OnCreate and first OnUpdate does not deliver a stale callback.
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
#pragma warning disable CA1711 // "Impl" suffix is intentional - matches sealed/abstract template pattern
        protected abstract void OnUpdateImpl();
#pragma warning restore CA1711

        /// <summary>
        /// Returns a capacity (<c>max(1, count)</c>) for sizing pre-allocated
        /// <c>NativeHashMap</c> / <c>NativeHashSet</c> / <c>NativeList</c>
        /// containers, and the actual entity count via <paramref name="actualCount"/>
        /// for empty-skip checks. The query's pending dependencies are completed by
        /// <c>CalculateEntityCount</c> — the <c>[CompletesDependency]</c> on this
        /// helper documents the sync and absorbs CIVIC218/CIVIC220 for callers.
        /// Use in throttled, one-shot, or post-load paths only; do not call from a
        /// hot <c>OnUpdate</c>.
        /// </summary>
        [CompletesDependency("CountForCapacity: query.CalculateEntityCount() reads chunk metadata to size a preallocated container; runs off the hot path (throttled / one-shot / post-load per call site)")]
        protected static int CountForCapacity(EntityQuery query, out int actualCount)
        {
            actualCount = query.CalculateEntityCount();
            return actualCount > 0 ? actualCount : 1;
        }

        /// <summary>
        /// Shortcut for callers that only need the capacity and not the actual count
        /// (e.g., <c>NativeHashMap</c> capacity-only resize without an empty-skip).
        /// </summary>
        [CompletesDependency("CountForCapacity: query.CalculateEntityCount() reads chunk metadata to size a preallocated container; runs off the hot path (throttled / one-shot / post-load per call site)")]
        protected static int CountForCapacity(EntityQuery query)
            => CountForCapacity(query, out _);
    }
}
