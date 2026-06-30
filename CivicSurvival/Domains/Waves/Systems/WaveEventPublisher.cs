using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Waves.Systems
{
    /// <summary>
    /// Facade for publishing wave-related events.
    /// Encapsulates EventBus calls for cleaner phase transition code.
    /// </summary>
    internal readonly struct WaveEventPublisher
    {
        private readonly IEventBus? m_Bus;

        public WaveEventPublisher(IEventBus? bus) => m_Bus = bus;

        public void NotifyPhaseChanged(GamePhase phase, int waveNumber)
            => m_Bus?.SafePublish(new ThreatNarrativeEvent(
                ThreatNarrativeEventType.WavePhaseChanged,
                Phase: phase,
                WaveNumber: waveNumber), "WaveExecutor");

        public void NotifyThreatAlert(int waveNumber, int threatCount)
            => m_Bus?.SafePublish(new ThreatNarrativeEvent(
                ThreatNarrativeEventType.ThreatAlert,
                WaveNumber: waveNumber,
                ThreatCount: threatCount), "WaveExecutor");

        public void NotifyWaveStarting(int waveNumber, int threatCount, WaveRole waveRole)
            => m_Bus?.SafePublish(new WaveStartingEvent(waveNumber, threatCount, waveRole), "WaveExecutor");

        // W7-M15 FIX: Forward BallisticOverride (was always defaulting to -1)
        public void NotifySpawnRequest(int threatCount, int waveNumber, WaveType waveType, int ballisticOverride = -1, WaveRole waveRole = WaveRole.Regular)
            => m_Bus?.SafePublish(new SpawnWaveRequestEvent(
                ThreatCount: threatCount,
                WaveNumber: waveNumber,
                WaveType: waveType,
                BallisticOverride: ballisticOverride,
                WaveRole: waveRole), "WaveExecutor");

        public void NotifyWaveEnded(int waveNumber, WaveRole waveRole, int intercepted, int hits, int shots, int casualties, int damageCost, long infraDamageCost, int crashed = 0,
            int droneIntercepted = 0, int droneHits = 0, int ballisticIntercepted = 0, int ballisticHits = 0, int roundsConsumed = 0, int missilesConsumed = 0)
        {
            // Settlement is owned by WarDamageDebtSystem after terminal damage/debt accounting.
            m_Bus?.SafePublish(new WaveEndedEvent(
                waveNumber, intercepted, hits, shots, casualties, damageCost, infraDamageCost, crashed, waveRole,
                droneIntercepted, droneHits, ballisticIntercepted, ballisticHits, roundsConsumed, missilesConsumed), "WaveExecutor");
        }

    }
}

