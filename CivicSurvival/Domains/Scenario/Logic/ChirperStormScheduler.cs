using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Scenario.Logic
{
    /// <summary>
    /// Schedules Chirper posts with delays to avoid spam and cooldown blocks.
    /// Each author can only post once per 60 seconds (Engine.Notifications.COOLDOWN_SOCIAL_POST).
    /// Posts are scheduled with increasing delays to create a "storm" effect.
    ///
    /// All messages are localized via SatireRegistry (NarrativeTriggerEvent pattern).
    /// </summary>
    public class ChirperStormScheduler
    {
        private static readonly LogContext Log = new("ChirperStormScheduler");
        public const float MaxDurationSeconds = 100f;
        private const float MAX_DELTA_SECONDS = 0.1f;

        private struct ScheduledTrigger
        {
            public float Delay;
            public string TriggerKey;

            public ScheduledTrigger(float delay, string triggerKey)
            {
                Delay = delay;
                TriggerKey = triggerKey;
            }
        }

        // ARCH-S-006: Static array is intentional design decision
        // Reasons for hardcoded array vs JSON:
        // 1. UI/UX choreography - precise timing critical for "storm" narrative effect
        // 2. Rarely modified (intro sequence is fixed content)
        // 3. Localization handled separately via NarrativeTriggers + SatireRegistry
        // 4. Avoids runtime loading overhead and error handling complexity
        // Trade-off: Compile-time data vs runtime flexibility (flexibility not needed here)

        // Each author appears MAX ONCE per 60 seconds to avoid cooldown block
        private static readonly ScheduledTrigger[] s_IntroStorm = new[]
        {
            // Wave 1: Immediate reactions (0-20s)
            new ScheduledTrigger(0f,  NarrativeTrigger.StormValera1.ToKey()),
            new ScheduledTrigger(3f,  NarrativeTrigger.StormMayor.ToKey()),
            new ScheduledTrigger(8f,  NarrativeTrigger.StormGrid.ToKey()),
            new ScheduledTrigger(15f, NarrativeTrigger.StormResident.ToKey()),

            // Wave 2: News & services (20-45s)
            new ScheduledTrigger(22f, NarrativeTrigger.StormNexta.ToKey()),
            new ScheduledTrigger(30f, NarrativeTrigger.StormHospital.ToKey()),
            new ScheduledTrigger(40f, NarrativeTrigger.StormIt.ToKey()),

            // Wave 3: Updates (60s+ - Valera can post again)
            new ScheduledTrigger(70f, NarrativeTrigger.StormValera2.ToKey()),
            new ScheduledTrigger(75f, NarrativeTrigger.StormBabcya.ToKey()),
            new ScheduledTrigger(90f, NarrativeTrigger.StormPetrenko.ToKey()),
        };

        private float m_ElapsedTime;
        private int m_NextPostIndex;
        private bool m_Active;

        private IEventBus? m_EventBus;

        public bool IsActive => m_Active;

        public void StartStorm()
        {
            RebindEventBus();
            m_Active = true;
            m_ElapsedTime = 0f;
            m_NextPostIndex = 0;
            Log.Info("Started intro storm (10 posts over 90s)");
        }

        private void RebindEventBus()
        {
            m_EventBus = ServiceRegistry.Instance.Require<IEventBus>();
        }

        public void Update(float deltaTime)
        {
            if (!m_Active) return;

#pragma warning disable CIVIC056 // Storm lasts ~2 min max, then deactivates
            m_ElapsedTime += System.Math.Clamp(deltaTime, 0f, MAX_DELTA_SECONDS);
#pragma warning restore CIVIC056

            if (m_NextPostIndex < s_IntroStorm.Length &&
                s_IntroStorm[m_NextPostIndex].Delay <= m_ElapsedTime)
            {
                var trigger = s_IntroStorm[m_NextPostIndex];
#pragma warning disable CIVIC242 // Multi-publisher by design — each system publishes distinct NarrativeTrigger keys
                m_EventBus.SafePublish(new NarrativeTriggerEvent(trigger.TriggerKey), "ChirperStormScheduler");
#pragma warning restore CIVIC242
                if (Log.IsDebugEnabled) Log.Debug($"Triggered: {trigger.TriggerKey} at {m_ElapsedTime:F1}s");
                m_NextPostIndex++;
            }

            if (m_NextPostIndex >= s_IntroStorm.Length)
            {
                m_Active = false;
                Log.Info("Intro storm complete");
            }
        }

        public void Stop()
        {
            m_Active = false;
        }

        // ===== Serialization Support =====

        public (float elapsedTime, int nextPostIndex, bool active) GetState()
        {
            return (m_ElapsedTime, m_NextPostIndex, m_Active);
        }

        public void RestoreState(float elapsedTime, int nextPostIndex, bool active)
        {
            m_ElapsedTime = float.IsFinite(elapsedTime) ? System.Math.Clamp(elapsedTime, 0f, MaxDurationSeconds) : 0f;
            m_NextPostIndex = System.Math.Clamp(nextPostIndex, 0, s_IntroStorm.Length);
            m_Active = active && m_NextPostIndex < s_IntroStorm.Length;
            if (m_Active)
                RebindEventBus();
        }
    }
}
