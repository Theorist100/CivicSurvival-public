using System;
using Unity.Entities;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Base
{
    /// <summary>
    /// Base class for systems that should only run every N frames.
    /// Inherits from CivicSystemBase for lazy-cached EventBus access and auto-profiling.
    /// Uses ThrottleHelper for shared throttle logic.
    ///
    /// Usage:
    /// public partial class MySystem : ThrottledSystemBase
    /// {
    ///     protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;
    ///
    ///     protected override void OnThrottledUpdate()
    ///     {
    ///         // Your logic here - runs every ~0.5 seconds
    ///         EventBus?.SafePublish(new MyEvent()); // lazy-cached from CivicSystemBase
    ///     }
    /// }
    ///
    /// For systems with pre-checks before throttling, override ShouldSkipUpdate():
    /// protected override bool ShouldSkipUpdate()
    /// {
    ///     return !SomeCondition; // return true to skip entirely
    /// }
    /// </summary>
    public abstract partial class ThrottledSystemBase : CivicSystemBase
    {
        private ThrottleHelper m_Throttle;
        private bool m_Initialized;
        private bool m_WasRunning;
#pragma warning disable CIVIC150 // First-frame-per-instance baseline; in-process save/load preserves m_WasRunning intentionally so OnBecameDisabled/OnBecameEnabled hooks do not double-fire after deserialize. Reset only on new process (fresh OnCreate).
        private bool m_TransitionInitialized;
#pragma warning restore CIVIC150
        // Lag spike protection: clamp delta to (throttle_interval × max_speed × safety)
        private const float MAX_GAME_SPEED = 3f;
        private const float DELTA_CLAMP_MARGIN = 2f;
        private float m_MaxThrottledDelta;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Register so PostLoadValidationSystem can rebase the throttle schedule
            // after deserialization without force-firing every throttled system.
            var plvs = World.GetExistingSystemManaged<PostLoadValidationSystem>();
            plvs?.RegisterThrottled(this);
        }
        private double m_LastThrottledTime = double.NegativeInfinity;
        private float m_ThrottledDeltaSeconds;

        /// <summary>
        /// Number of frames between updates.
        /// Override to set custom interval (default: UPDATE_INTERVAL_500_MS = 30).
        /// Use Engine.Timing constants for consistency.
        /// </summary>
#pragma warning disable CA1721 // Property name matches method - this is a virtual property pattern
        protected virtual int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;
#pragma warning restore CA1721

        /// <summary>
        /// Override to share throttle phase with another system.
        /// Systems with the same ThrottlePhaseKey and UpdateInterval will fire on the same frame.
        /// Default: GetType().Name (each system fires independently).
        /// </summary>
        protected virtual string ThrottlePhaseKey => GetType().Name;

        /// <summary>
        /// Optional pre-check before throttle logic.
        /// Return true to skip this frame entirely (no counter increment).
        /// Use for early-exit conditions like "no data available".
        /// </summary>
        protected virtual bool ShouldSkipUpdate() => false;

        /// <summary>
        /// Optional per-frame override to force-fire <see cref="OnThrottledUpdate"/> this frame
        /// regardless of the throttle counter. Default: false (normal throttle applies).
        ///
        /// Use for subclasses that must run on a specific vanilla-writer boundary the throttle
        /// interval can miss (e.g., <c>PowerCapacityResolverSystem</c> must rewrite
        /// <c>ElectricityFlowEdge.m_Capacity</c> on frame %128 == 0, right after
        /// <c>PowerPlantAISystem</c> and before <c>ElectricityFlowSystem.PrepareEdgesJob</c>
        /// on frame %128 == 1, otherwise the solver snapshots stale producer capacity for
        /// the next 128-frame cycle).
        ///
        /// When this returns true:
        /// - <see cref="OnThrottledUpdate"/> fires this frame.
        /// - The throttle counter is reset so the next regular fire is one full
        ///   <see cref="UpdateInterval"/> away (prevents close double-fire on adjacent frames).
        /// - <see cref="ThrottledDeltaSeconds"/> reflects time since the previous fire
        ///   (regular or bypass).
        /// - <see cref="ShouldSkipUpdate"/> is honored first — bypass cannot override the
        ///   skip gate or its lifecycle transition hooks.
        /// </summary>
        protected virtual bool ShouldBypassThrottle() => false;

        /// <summary>
        /// Actual seconds elapsed since the last throttled update.
        /// Replaces the inaccurate <c>UpdateInterval * FRAME_TIME_SECONDS</c> pattern.
        /// </summary>
        protected float ThrottledDeltaSeconds => m_ThrottledDeltaSeconds;

        protected sealed override void OnUpdateImpl()
        {
            double now = World.Time.ElapsedTime;

            // Lazy init (virtual property can't be used in field initializer)
            if (!m_Initialized)
            {
                int phase = StableTypeHash(ThrottlePhaseKey);
                m_Throttle = new ThrottleHelper(UpdateInterval, phase);
                m_MaxThrottledDelta = (UpdateInterval / Engine.Timing.SIMULATION_FPS) * MAX_GAME_SPEED * DELTA_CLAMP_MARGIN;
                m_LastThrottledTime = now;
                m_Initialized = true;
            }

            // Optional pre-check (e.g., "no districts to process")
            bool shouldSkip = ShouldSkipUpdate();

            // Lifecycle transition hooks — guaranteed single call on state change.
            // Override OnBecameDisabled to clean up output singletons/buffers/tags.
            if (!m_TransitionInitialized)
            {
                // First frame: set baseline without firing hooks
                m_WasRunning = !shouldSkip;
                m_TransitionInitialized = true;
            }
            else if (m_WasRunning && shouldSkip)
            {
                // FIX L1: Set flag BEFORE call — exception in OnBecameDisabled would leave
                // m_WasRunning=true → infinite retry loop on next frame
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

            // Use shared throttle helper, with optional per-subclass bypass for critical frames
            // (vanilla-writer ordering boundaries that the throttle interval can miss).
            bool bypassThrottle = ShouldBypassThrottle();
            if (!bypassThrottle)
            {
                if (!m_Throttle.ShouldUpdate())
                    return;
            }
            else
            {
                // Realign counter so the next regular fire is one full UpdateInterval away
                // (avoids close double-fire if bypass and throttle would land on adjacent frames).
                m_Throttle.ResetToFullInterval();
            }

            // FIX #164-B: After save/load, ElapsedTime resets to ~0 but m_LastThrottledTime
            // retains the old value (e.g., 500.0). Without guard: delta = 0 - 500 = -500f,
            // causing reversed GameRate.ScalePerDay calculations in ~30+ subclasses.
            // Applies equally to bypass fires — delta is from previous fire of either kind.
            m_ThrottledDeltaSeconds = Math.Min(m_MaxThrottledDelta, Math.Max(0f, (float)(now - m_LastThrottledTime)));
            m_LastThrottledTime = now;

            // Actual work (auto-profiled by CivicSystemBase)
            OnThrottledUpdate();
        }

        /// <summary>
        /// Called every UpdateInterval frames.
        /// Implement your system logic here.
        /// </summary>
        protected abstract void OnThrottledUpdate();

        /// <summary>
        /// Called exactly once when ShouldSkipUpdate transitions from false to true.
        /// Override to clear output singletons, buffers, tags that downstream systems read.
        /// Without cleanup, downstream reads ghost data indefinitely.
        /// </summary>
        protected virtual void OnBecameDisabled() { }

        /// <summary>
        /// Called exactly once when ShouldSkipUpdate transitions from true to false.
        /// Override to re-initialize caches that may be stale after a disabled period.
        /// </summary>
        protected virtual void OnBecameEnabled() { }

        /// <summary>
        /// Reset m_WasRunning when system is disabled via Enabled=false.
        /// Without this, OnBecameEnabled is skipped on re-enable (m_WasRunning stays true from before disable).
        /// </summary>
        protected override void OnStopRunning()
        {
            m_WasRunning = false;
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            var plvs = World.GetExistingSystemManaged<PostLoadValidationSystem>();
            plvs?.UnregisterThrottled(this);
            base.OnDestroy();
        }

        /// <summary>
        /// Reset throttle counter to this system's stagger phase.
        /// Use ForceNextUpdate() only when an event needs an immediate one-shot wake.
        /// Safe to call before first update (no-op if not initialized).
        /// </summary>
        protected void ResetThrottleCounter()
        {
            if (!m_Initialized) return;  // FIX: Guard against use before init
            m_Throttle.Reset();
#pragma warning disable CIVIC034 // Ephemeral reset, not persisted — prevents stale delta after external throttle reset
            m_LastThrottledTime = World.Time.ElapsedTime;
#pragma warning restore CIVIC034
        }

        /// <summary>
        /// Rebase post-load throttle state to this system's stagger phase.
        /// This keeps normal throttled systems distributed across frames after load
        /// while clearing stale force flags and elapsed-time baselines.
        /// </summary>
        public void ResetPostLoadThrottleSchedule()
        {
            ResetThrottleCounter();
        }

        /// <summary>
        /// Force this system to fire on the next frame regardless of throttle counter.
        /// Called by phase-transition systems when this system's output is needed immediately
        /// (e.g., wave phase change, act transition, grid collapse).
        /// Safe to call before first update (no-op if not initialized).
        /// </summary>
        public void ForceNextUpdate()
        {
            if (!m_Initialized) return;
            m_Throttle.ForceNextFire();
            // S10-02 FIX: Reset timestamp so ThrottledDeltaSeconds
            // computes a small positive value, not zero.
#pragma warning disable CIVIC034 // Ephemeral reset, not persisted — next delta = (now - now) ≈ frame time
            m_LastThrottledTime = World.Time.ElapsedTime;
#pragma warning restore CIVIC034
        }

        // FNV-style hash primes for stagger phase computation
        private const int HASH_SEED = 17;
        private const int HASH_PRIME = 31;

        /// <summary>
        /// Deterministic hash from type name for stagger phase.
        /// Does not use string.GetHashCode (non-deterministic across runs).
        /// </summary>
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

        private void InvokeTransitionHook(Action hook, string hookName)
        {
            try
            {
                hook();
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"{GetType().Name}.{hookName} failed: {ex}");
            }
        }
    }
}
