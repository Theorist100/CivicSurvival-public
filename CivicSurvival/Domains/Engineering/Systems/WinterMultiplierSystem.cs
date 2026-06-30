using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// Winter Multiplier Observer - tracks winter severity and publishes events.
    ///
    /// ARCHITECTURE NOTE:
    /// This system is an OBSERVER ONLY. It does NOT modify ElectricityConsumer.
    /// The actual consumption multiplier is applied via Harmony patch on
    /// AdjustElectricityConsumptionSystem.GetTemperatureMultiplier().
    /// See: Patches/WinterMultiplierPatch.cs
    ///
    /// This separation avoids:
    /// 1. Race conditions with vanilla Burst jobs
    /// 2. Conflicts with vanilla's time-sliced consumption updates
    /// 3. ECB complexity for high-frequency writes
    ///
    /// This system provides:
    /// - Event publishing for narrative/scenario systems
    /// - UI state tracking (current multiplier)
    /// - Logging for debugging
    ///
    /// Temperature ranges (same formula as patch):
    /// - Above 10C: x1.0 (no multiplier)
    /// - 0C to 10C: linear interpolation to mid
    /// - -10C to 0C: linear interpolation to max
    /// - Below -10C: max multiplier
    ///
    /// Difficulty scaling (via WinterSeverity):
    /// - Easy (0.67): ~2.0x total
    /// - Normal (1.0): ~3.0x total
    /// - Hardcore (1.5): ~4.0x total
    /// </summary>
    [ActIndependent]
    public partial class WinterMultiplierSystem : ThrottledSystemBase
    {
        private const float FALLBACK_TEMPERATURE = 15f;

        private static readonly LogContext Log = new("WinterMultiplierSystem");

        // Less frequent updates - we're just observing, not modifying
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_2_SECONDS;

        [System.NonSerialized] private ClimateState? m_ClimateAdapter;
        private float m_LastMultiplier = 1.0f;
        private bool m_WasWinterActive; // temperature state transition detection (cold↔warm)

        // M2 FIX: Cache ModSettings (avoid lookup in hot path)
        private ModSettings? m_Settings;
        [System.NonSerialized] private bool m_TemperatureWarningLogged;

        protected override bool ShouldSkipUpdate()
        {
            return m_Settings == null || m_ClimateAdapter == null || !m_Settings.WinterMultiplierEnabled;
        }

        /// <summary>
        /// G10-10: teardown-safe clear of runtime winter state — NO event publish.
        /// Safe in any context including OnStopRunning/OnDestroy (world unload), where
        /// publishing a gameplay transition to half-disposed subscribers is invalid.
        /// </summary>
        private void ResetWinterRuntime()
        {
            m_WasWinterActive = false;
            WriteSingletonWinterActive(false);
            // Reset to base (no winter) so a crisis transition re-triggers on re-enable.
            // MaxValue would persist via the manual codec and fail the
            // previousMultiplier < crisisThreshold check on first update.
            m_LastMultiplier = 1.0f;
        }

        /// <summary>
        /// Feature toggled off while the world is live — a genuine winter→non-winter
        /// gameplay transition. This is the ONLY ClearWinterState-class path that may
        /// publish WinterEnded (FIX S16_CODE3:80: only if winter was actually active);
        /// teardown (OnStopRunning/OnDestroy) must not, hence the split (G10-10).
        /// </summary>
        protected override void OnBecameDisabled()
        {
            bool wasWinter = m_WasWinterActive;
            ResetWinterRuntime();
            if (wasWinter && m_Settings != null)
                EventBus?.SafePublish(new InfraEvent(InfraEventType.WinterEnded), "WinterMultiplierSystem");
            Log.Info("Winter Multiplier disabled");
        }

        protected override void OnStopRunning()
        {
            ResetWinterRuntime();
            Log.Info("Winter Multiplier stopped");
            base.OnStopRunning();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info("Created (observer mode)");

            // A2 FIX 2b: Own singleton for winter state (replaces PowerGridSingleton.IsWinterActive)
            WinterStateSingleton.EnsureExists(EntityManager);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_ClimateAdapter ??= ServiceRegistry.Instance.Require<ClimateState>();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
        }

        protected override void OnThrottledUpdate()
        {
            float temperature = GetCurrentTemperature();
            float winterSeverity = m_Settings?.WinterSeverity ?? 1.0f;
            float multiplier = CalculateEffectiveMultiplier(temperature, winterSeverity);

            var engCfg = BalanceConfig.Current.Engineering;
            float previousMultiplier = m_LastMultiplier;
            bool isWinterActive = multiplier > engCfg.WinterMultMid;
            float crisisThreshold = math.max(
                engCfg.WinterCrisisThreshold,
                engCfg.WinterMultMid + 0.1f);

            // C9: Epsilon should suppress noise, not swallow threshold crossings.
            bool crossedWinterBoundary = isWinterActive != m_WasWinterActive;
            bool crossedCrisisThreshold = multiplier >= crisisThreshold && previousMultiplier < crisisThreshold;
            bool significantChange = math.abs(multiplier - previousMultiplier) >= engCfg.MultiplierChangeEpsilon;
            if (!significantChange && !crossedWinterBoundary && !crossedCrisisThreshold)
                return;

            m_LastMultiplier = multiplier;

            // Publish state change events
            if (crossedWinterBoundary)
            {
                m_WasWinterActive = isWinterActive;
                WriteSingletonWinterActive(isWinterActive);

                if (isWinterActive)
                {
                    Log.Info($"Winter activated: temperature {temperature:F1}C, multiplier x{multiplier:F2}");
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.WinterActivated), "WinterMultiplierSystem");
                }
                else
                {
                    Log.Info($"Winter ended: temperature {temperature:F1}C");
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.WinterEnded), "WinterMultiplierSystem");
                }
            }

            // Crisis event when multiplier is severe (x2.5+ total with vanilla's base)
            if (crossedCrisisThreshold)
            {
                Log.Info($"Winter Crisis: temperature {temperature:F1}C, multiplier x{multiplier:F2}");
                EventBus?.SafePublish(new InfraEvent(InfraEventType.WinterCrisis), "WinterMultiplierSystem");
            }
        }

        private float GetCurrentTemperature()
        {
            float temperature = m_ClimateAdapter!.Current.Temperature;
            if (math.isfinite(temperature))
                return temperature;

            if (!m_TemperatureWarningLogged)
            {
                Log.Warn($"ClimateState returned non-finite temperature ({temperature}) — using fallback");
                m_TemperatureWarningLogged = true;
            }
            return FALLBACK_TEMPERATURE;
        }

        /// <summary>
        /// Calculate the effective multiplier that will be applied by the Harmony patch.
        /// Delegates to shared WinterAmplificationCalculator (single source of truth).
        /// </summary>
        private float CalculateEffectiveMultiplier(float temperature, float winterSeverity)
        {
            return WinterAmplificationCalculator.Calculate(temperature, winterSeverity);
        }

        /// <summary>
        /// Get current multiplier for UI display.
        /// </summary>
        public float CurrentMultiplier => m_LastMultiplier;

        /// <summary>
        /// Is winter currently active (multiplier above threshold).
        /// </summary>
        public bool IsWinterActive => m_WasWinterActive;

        private void WriteSingletonWinterActive(bool active)
        {
            if (SystemAPI.TryGetSingletonRW<WinterStateSingleton>(out var rw))
                rw.ValueRW.IsWinterActive = active;
            else
                Log.Warn($"WriteSingletonWinterActive({active}): WinterStateSingleton not found — write dropped");
        }

        protected override void OnDestroy()
        {
            ResetWinterRuntime();
            Log.Info("Destroyed");
            base.OnDestroy();
        }
    }
}
