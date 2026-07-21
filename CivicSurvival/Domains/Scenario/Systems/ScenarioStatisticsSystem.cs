using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Aggregates statistics from all domains into ScenarioState.
    /// Stateless - reacts to events and writes to ScenarioStateMachine.
    /// No serialization needed (statistics persist in ScenarioState).
    /// </summary>
    [ActIndependent]
    public partial class ScenarioStatisticsSystem : EventDrivenSystemBase
    {
        private static readonly LogContext Log = new("ScenarioStatisticsSystem");

        private ScenarioStateMachine m_StateMachine = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            SubscribeBufferedUntilReady<WaveEndedEvent>(OnWaveEnded);
            SubscribeBufferedUntilReady<BuildingDamagedEvent>(OnBuildingDamaged);
            SubscribeBufferedUntilReady<FirstStrikeCascadeEvent>(OnFirstStrikeCascade);
            SubscribeBufferedUntilReady<BlackoutRecoveredEvent>(OnBlackoutRecovery);

            // CIVIC243 FIX: Wire RecordRefugeesReceived / RecordCitizensLeft
            SubscribeBufferedUntilReady<RefugeesReceivedEvent>(OnRefugeesReceived);
            SubscribeBufferedUntilReady<CitizensLeftEvent>(OnCitizensLeft);
            SubscribeBufferedUntilReady<DonorEvent>(OnDonorEvent);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateMachine ??= FeatureRegistry.Instance.Require<ScenarioStateMachine>();
            MarkEventHandlersReady();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
            UnsubscribeSafe<BuildingDamagedEvent>(OnBuildingDamaged);
            UnsubscribeSafe<FirstStrikeCascadeEvent>(OnFirstStrikeCascade);
            UnsubscribeSafe<BlackoutRecoveredEvent>(OnBlackoutRecovery);
            UnsubscribeSafe<RefugeesReceivedEvent>(OnRefugeesReceived);
            UnsubscribeSafe<CitizensLeftEvent>(OnCitizensLeft);
            UnsubscribeSafe<DonorEvent>(OnDonorEvent);

            base.OnDestroy();
        }

        // ===== Event Handlers =====

        private void OnWaveEnded(WaveEndedEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;
            if (evt.WaveRole == WaveRole.Intro) return; // Intro is not a real defense; don't count toward victory
            m_StateMachine.RecordWaveDefended(evt.Intercepted);
        }

        private void OnBuildingDamaged(BuildingDamagedEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;
            m_StateMachine.RecordBuildingsDamaged();
        }

        private void OnFirstStrikeCascade(FirstStrikeCascadeEvent evt)
        {
            // PlannedHits NOT counted here — actual hits arrive via BuildingDamagedEvent
            // from ThreatDamageSystem. Counting both would double-count intro wave damage.
            // FirstStrikeCascadeEvent is for narrative subscribers only.
        }

        private void OnBlackoutRecovery(BlackoutRecoveredEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;
            m_StateMachine.RecordBlackoutRecovery();
        }

        // CIVIC243 FIX: Wire population statistics
        private void OnRefugeesReceived(RefugeesReceivedEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;
            m_StateMachine.RecordRefugeesReceived(evt.Count);
        }

        private void OnCitizensLeft(CitizensLeftEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;
            m_StateMachine.RecordCitizensLeft(evt.Count);
        }

        private void OnDonorEvent(DonorEvent evt)
        {
            if (m_StateMachine.IsDefeated) return;

            if (evt.Type == DonorEventType.AidPackageReceived)
            {
                m_StateMachine.RecordDonorAidReceived();
            }
        }

        // ===== Public API (proxies to ScenarioState) =====

        public int TotalWavesDefended => m_StateMachine.State.WavesDefended;
        public int TotalMissilesIntercepted => m_StateMachine.State.MissilesIntercepted;
        public int TotalBlackoutRecoveries => m_StateMachine.State.BlackoutRecoveries;
        public int TotalBuildingsDamaged => m_StateMachine.State.BuildingsDamaged;
    }
}
