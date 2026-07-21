using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for UI systems that should only run every N frames.
    /// Inherits from CivicUISystemBase for lazy-cached EventBus access.
    /// Uses ThrottleHelper for shared throttle logic.
    ///
    /// Usage:
    /// public partial class MyUISystem : ThrottledUISystemBase
    /// {
    ///     protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;
    ///
    ///     protected override void OnThrottledUpdate()
    ///     {
    ///         // Your logic here - runs every ~0.5 seconds
    ///         EventBus?.SafePublish(new MyEvent()); // lazy-cached from CivicUISystemBase
    ///     }
    /// }
    /// </summary>
    public abstract partial class ThrottledUISystemBase : CivicUISystemBase
    {
        private ThrottleHelper m_Throttle;
        private bool m_Initialized;
        private bool m_WasRunning;
        private bool m_TransitionInitialized;
        private double m_LastThrottledTime = double.NegativeInfinity;
        private float m_ThrottledDeltaSeconds;
        private float m_MaxThrottledDelta;

        // Lag spike protection: clamp delta to (throttle_interval × safety)
        // UI uses realtime — no game speed multiplier
        private const float DELTA_CLAMP_MARGIN = 2f;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Register so PostLoadValidationSystem can rebase the throttle schedule
            // after deserialization without force-firing every throttled UI system.
            var plvs = World.GetExistingSystemManaged<PostLoadValidationSystem>();
            plvs?.RegisterThrottledUI(this);
        }

        /// <summary>
        /// Number of frames between updates.
        /// Override to set custom interval (default: UPDATE_INTERVAL_500_MS = 30).
        /// Use Engine.Timing constants for consistency.
        /// </summary>
#pragma warning disable CA1721 // Property name matches method - this is a virtual property pattern
        protected virtual int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;
#pragma warning restore CA1721

        /// <summary>
        /// Override to share throttle phase with another UI system.
        /// Systems with the same ThrottlePhaseKey and UpdateInterval will fire on the same frame.
        /// Default: GetType().Name (each UI system fires independently).
        /// </summary>
        protected virtual string ThrottlePhaseKey => GetType().Name;

        /// <summary>
        /// Optional pre-check before throttle logic.
        /// Return true to skip this frame entirely (no counter increment).
        /// </summary>
        protected virtual bool ShouldSkipUpdate() => false;

        /// <summary>
        /// Actual seconds elapsed since the last throttled update.
        /// Matches ThrottledSystemBase API for consistent time delta access.
        /// UI variant uses realtime, so it advances while the game is paused.
        /// </summary>
        protected float ThrottledDeltaSeconds => m_ThrottledDeltaSeconds;

        protected sealed override void OnUpdateImpl()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;

            // Lazy init (virtual property can't be used in field initializer)
            if (!m_Initialized)
            {
                int phase = StableTypeHash(ThrottlePhaseKey);
                m_Throttle = new ThrottleHelper(UpdateInterval, phase);
                m_MaxThrottledDelta = (UpdateInterval / Engine.Timing.SIMULATION_FPS) * DELTA_CLAMP_MARGIN;
                m_LastThrottledTime = now;
                m_Initialized = true;
            }

            // Optional pre-check
            bool shouldSkip = ShouldSkipUpdate();

            // Lifecycle transition hooks (mirrors ThrottledSystemBase)
            if (!m_TransitionInitialized)
            {
                m_WasRunning = !shouldSkip;
                m_TransitionInitialized = true;
            }
            else if (m_WasRunning && shouldSkip)
            {
                // FIX L1: Set flag BEFORE call — exception leaves consistent state
                m_WasRunning = false;
                InvokeTransitionHook(OnBecameDisabled, nameof(OnBecameDisabled));
            }
            else if (!m_WasRunning && !shouldSkip)
            {
                m_WasRunning = true;
                InvokeTransitionHook(OnBecameEnabled, nameof(OnBecameEnabled));
            }

            if (shouldSkip)
                return;

            // Use shared throttle helper
            if (!m_Throttle.ShouldUpdate())
                return;

            // Track actual elapsed time between throttled updates
            m_ThrottledDeltaSeconds = System.Math.Min(m_MaxThrottledDelta, System.Math.Max(0f, (float)(now - m_LastThrottledTime)));
            m_LastThrottledTime = now;

            // Actual work (auto-profiled by CivicUISystemBase)
            OnThrottledUpdate();
        }

        /// <summary>
        /// Called every UpdateInterval frames.
        /// Implement your system logic here.
        /// </summary>
        protected abstract void OnThrottledUpdate();

        /// <summary>
        /// Called exactly once when ShouldSkipUpdate transitions from false to true.
        /// Override to clear output data that downstream systems read.
        /// </summary>
        protected virtual void OnBecameDisabled() { }

        /// <summary>
        /// Called exactly once when ShouldSkipUpdate transitions from true to false.
        /// Override to re-initialize caches that may be stale after a disabled period.
        /// </summary>
        protected virtual void OnBecameEnabled() { }

        protected override void OnStopRunning()
        {
            m_WasRunning = false;
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            World.GetExistingSystemManaged<PostLoadValidationSystem>()?.UnregisterThrottledUI(this);
            base.OnDestroy();
        }

        /// <summary>
        /// Reset throttle counter to this system's stagger phase.
        /// Use ForceNextUpdate() only when an event needs an immediate one-shot wake.
        /// </summary>
        protected void ResetThrottleCounter()
        {
            if (!m_Initialized) return;
            m_Throttle.Reset();
            m_LastThrottledTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>
        /// Rebase post-load throttle state to this system's stagger phase.
        /// This keeps normal UI throttled systems distributed across frames after load
        /// while clearing stale force flags and elapsed-time baselines.
        /// </summary>
        public void ResetPostLoadThrottleSchedule()
        {
            ResetThrottleCounter();
        }

        /// <summary>
        /// Force this system to fire on the next frame regardless of throttle counter.
        /// Called by event/transition systems when UI state must refresh immediately.
        /// Safe to call before first update (no-op if not initialized).
        /// </summary>
        public void ForceNextUpdate()
        {
            if (!m_Initialized) return;
            m_Throttle.ForceNextFire();
            // UI systems use realtime, not ECS time
            m_LastThrottledTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        private void InvokeTransitionHook(System.Action hook, string hookName)
        {
            try
            {
                hook();
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"{GetType().Name}.{hookName} failed: {ex}");
            }
        }

        private const int HASH_SEED = 17;
        private const int HASH_PRIME = 31;

        private static int StableTypeHash(string name)
        {
            unchecked
            {
                int hash = HASH_SEED;
                for (int i = 0; i < name.Length; i++)
                    hash = hash * HASH_PRIME + name[i];
                return hash & 0x7FFFFFFF;
            }
        }
    }
}
