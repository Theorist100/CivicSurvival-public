using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    internal sealed class CognitiveTelemetryListener : IDisposable
    {
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public CognitiveTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<HeroDeployedEvent>(OnHeroDeployed);
            m_EventBus.Subscribe<HeroRecalledEvent>(OnHeroRecalled);
            m_EventBus.Subscribe<HeroModeChangedEvent>(OnHeroModeChanged);
            m_EventBus.Subscribe<CognitiveCompromisedEvent>(OnCognitiveCompromised);
            m_EventBus.Subscribe<CognitiveRecoveredEvent>(OnCognitiveRecovered);
            m_EventBus.Subscribe<TelemarathonShockEvent>(OnTelemarathonShock);
            m_EventBus.Subscribe<TelemarathonModeChangedEvent>(OnTelemarathonModeChanged);
            m_EventBus.Subscribe<BuckwheatDistributedEvent>(OnBuckwheatDistributed);
            m_EventBus.Subscribe<BuckwheatProcuredEvent>(OnBuckwheatProcured);
            m_EventBus.Subscribe<PsyOpsAttackEvent>(OnPsyOpsAttack);
            m_EventBus.Subscribe<PsyWaveResultEvent>(OnPsyWaveResult);
            m_EventBus.Subscribe<PropagandaCenterPlacedEvent>(OnCenterPlaced);
            m_EventBus.Subscribe<PropagandaCenterUpgradedEvent>(OnCenterUpgraded);
            m_EventBus.Subscribe<InternetModeChangedEvent>(OnInternetModeChanged);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<HeroDeployedEvent>(OnHeroDeployed);
            m_EventBus.Unsubscribe<HeroRecalledEvent>(OnHeroRecalled);
            m_EventBus.Unsubscribe<HeroModeChangedEvent>(OnHeroModeChanged);
            m_EventBus.Unsubscribe<CognitiveCompromisedEvent>(OnCognitiveCompromised);
            m_EventBus.Unsubscribe<CognitiveRecoveredEvent>(OnCognitiveRecovered);
            m_EventBus.Unsubscribe<TelemarathonShockEvent>(OnTelemarathonShock);
            m_EventBus.Unsubscribe<TelemarathonModeChangedEvent>(OnTelemarathonModeChanged);
            m_EventBus.Unsubscribe<BuckwheatDistributedEvent>(OnBuckwheatDistributed);
            m_EventBus.Unsubscribe<BuckwheatProcuredEvent>(OnBuckwheatProcured);
            m_EventBus.Unsubscribe<PsyOpsAttackEvent>(OnPsyOpsAttack);
            m_EventBus.Unsubscribe<PsyWaveResultEvent>(OnPsyWaveResult);
            m_EventBus.Unsubscribe<PropagandaCenterPlacedEvent>(OnCenterPlaced);
            m_EventBus.Unsubscribe<PropagandaCenterUpgradedEvent>(OnCenterUpgraded);
            m_EventBus.Unsubscribe<InternetModeChangedEvent>(OnInternetModeChanged);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnHeroDeployed(HeroDeployedEvent evt)
            => Record(Cognitive.HeroDeployed, new CognitiveHeroDeployedData { Mode = evt.Mode, Cost = evt.Cost });

        private void OnHeroRecalled(HeroRecalledEvent evt)
            => Record(Cognitive.HeroRecalled, new CognitiveHeroRecalledData());

        private void OnHeroModeChanged(HeroModeChangedEvent evt)
            => Record(Cognitive.HeroModeChanged, new CognitiveHeroModeChangedData
            {
                FromMode = evt.FromMode,
                ToMode = evt.ToMode
            });

        private void OnCognitiveCompromised(CognitiveCompromisedEvent evt)
            => Record(Cognitive.DistrictCompromised, new CognitiveDistrictCompromisedData
            {
                DistrictIndex = evt.DistrictIndex,
                Integrity = evt.Integrity
            });

        private void OnCognitiveRecovered(CognitiveRecoveredEvent evt)
            => Record(Cognitive.DistrictRecovered, new CognitiveDistrictRecoveredData
            {
                DistrictIndex = evt.DistrictIndex,
                Integrity = evt.Integrity
            });

        private void OnTelemarathonShock(TelemarathonShockEvent evt)
            => Record(Cognitive.TelemarathonShock, new CognitiveTelemarathonShockData { TrustAfter = evt.TrustAfter });

        private void OnTelemarathonModeChanged(TelemarathonModeChangedEvent evt)
            => Record(Cognitive.TelemarathonModeChanged, new CognitiveTelemarathonModeChangedData
            {
                FromMode = evt.OldMode.ToString().ToSnakeCase(),
                ToMode = evt.NewMode.ToString().ToSnakeCase()
            });

        private void OnBuckwheatDistributed(BuckwheatDistributedEvent evt)
            => Record(Cognitive.BuckwheatDistribution, new CognitiveBuckwheatDistributionData
            {
                DistrictIndex = evt.DistrictIndex,
                TonsRemaining = evt.TonsRemaining,
                TrustBoost = evt.TrustBoost
            });

        private void OnBuckwheatProcured(BuckwheatProcuredEvent evt)
            => Record(Cognitive.BuckwheatProcurement, new CognitiveBuckwheatProcurementData
            {
                TonsProcured = evt.TonsProcured,
                Cost = evt.Cost,
                ReserveTotal = evt.ReserveTotal
            });

        private void OnPsyOpsAttack(PsyOpsAttackEvent evt)
            => Record(Cognitive.PsyopsAttack, new CognitivePsyopsAttackData
            {
                AttackType = evt.Type.ToString().ToSnakeCase(),
                TargetStratum = evt.Target.ToString().ToSnakeCase(),
                Wave = evt.Wave,
                Intensity = evt.Intensity
            });

        private void OnPsyWaveResult(PsyWaveResultEvent evt)
            => Record(Cognitive.PsyWaveResult, new CognitivePsyWaveResultData
            {
                Wave = evt.Wave,
                RaidCount = evt.RaidCount,
                Propaganda = evt.Propaganda,
                Rumor = evt.Rumor,
                FakeVideo = evt.FakeVideo,
                Blunted = evt.Blunted,
                // StrataMask bit layout: Poor=1, Middle=2, Wealthy=4 (CognitiveUISystem.StratumBit).
                TargetsPoor = (evt.StrataMask & 1) != 0,
                TargetsMiddle = (evt.StrataMask & 2) != 0,
                TargetsWealthy = (evt.StrataMask & 4) != 0,
                PeakIntensity = evt.PeakIntensity,
                MediaTrust = evt.MediaTrust,
                HouseholdsImpact = evt.HouseholdsImpact,
                FoodRunActive = evt.FoodRunActive
            });

        private void OnCenterPlaced(PropagandaCenterPlacedEvent evt)
            => Record(Cognitive.CenterPlaced, new CognitiveCenterPlacedData { Cost = evt.Cost });

        private void OnCenterUpgraded(PropagandaCenterUpgradedEvent evt)
            => Record(Cognitive.CenterUpgraded, new CognitiveCenterUpgradedData { Tier = evt.Tier, Cost = evt.Cost });

        private void OnInternetModeChanged(InternetModeChangedEvent evt)
            => Record(Cognitive.InternetModeChanged, new CognitiveInternetModeChangedData
            {
                FromMode = evt.FromMode.ToString().ToSnakeCase(),
                ToMode = evt.ToMode.ToString().ToSnakeCase()
            });
    }
}
