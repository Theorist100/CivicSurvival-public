using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    internal sealed class ThreatTelemetryListener : IDisposable
    {
        private static readonly ThreatImpactData s_ThreatImpactDrone = new() { IsBallistic = false };
        private static readonly ThreatImpactData s_ThreatImpactBallistic = new() { IsBallistic = true };
        private static readonly ThreatInterceptData s_ThreatInterceptDrone = new() { IsBallistic = false };
        private static readonly ThreatInterceptData s_ThreatInterceptBallistic = new() { IsBallistic = true };

        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public ThreatTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<ThreatNarrativeEvent>(OnThreatNarrativeEvent);
            m_EventBus.Subscribe<ThreatsSpawnedEvent>(OnThreatsSpawned);
            m_EventBus.Subscribe<WaveEndedEvent>(OnWaveEnded);
            m_EventBus.Subscribe<ThreatImpactEvent>(OnThreatImpact);
            m_EventBus.Subscribe<ThreatInterceptEvent>(OnThreatIntercept);
            m_EventBus.Subscribe<AAResupplyEvent>(OnAAResupplyEvent);
            m_EventBus.Subscribe<BuildingDamagedEvent>(OnBuildingDamaged);
            m_EventBus.Subscribe<FirstStrikeCascadeEvent>(OnFirstStrikeCascade);
            m_EventBus.Subscribe<InfrastructureCollapseEvent>(OnInfrastructureCollapse);
            m_EventBus.Subscribe<ObstacleIndexOobEvent>(OnObstacleIndexOob);
            m_EventBus.Subscribe<SpatialIndexOobEvent>(OnSpatialIndexOob);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<ThreatNarrativeEvent>(OnThreatNarrativeEvent);
            m_EventBus.Unsubscribe<ThreatsSpawnedEvent>(OnThreatsSpawned);
            m_EventBus.Unsubscribe<WaveEndedEvent>(OnWaveEnded);
            m_EventBus.Unsubscribe<ThreatImpactEvent>(OnThreatImpact);
            m_EventBus.Unsubscribe<ThreatInterceptEvent>(OnThreatIntercept);
            m_EventBus.Unsubscribe<AAResupplyEvent>(OnAAResupplyEvent);
            m_EventBus.Unsubscribe<BuildingDamagedEvent>(OnBuildingDamaged);
            m_EventBus.Unsubscribe<FirstStrikeCascadeEvent>(OnFirstStrikeCascade);
            m_EventBus.Unsubscribe<InfrastructureCollapseEvent>(OnInfrastructureCollapse);
            m_EventBus.Unsubscribe<ObstacleIndexOobEvent>(OnObstacleIndexOob);
            m_EventBus.Unsubscribe<SpatialIndexOobEvent>(OnSpatialIndexOob);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnThreatNarrativeEvent(ThreatNarrativeEvent evt)
        {
            Record(Threat.Narrative, new ThreatNarrativeData
            {
                Subtype = TelemetryMappers.MapThreatNarrativeSubtype(evt.Type),
                WaveNumber = evt.WaveNumber > 0 ? evt.WaveNumber : null,
                ThreatCount = evt.ThreatCount > 0 ? evt.ThreatCount : null,
                LostMw = evt.LostMW > 0 ? evt.LostMW : null,
                RemainingMw = evt.RemainingMW > 0 ? evt.RemainingMW : null
            });
        }

        private void OnThreatsSpawned(ThreatsSpawnedEvent evt)
        {
            Record(Threat.Spawned, new ThreatSpawnedData
            {
                ShahedCount = evt.ShahedCount,
                BallisticCount = evt.BallisticCount,
                WaveNumber = evt.WaveNumber
            });
        }

        private void OnWaveEnded(WaveEndedEvent evt)
        {
            var successRate = evt.Intercepted + evt.Hits > 0
                ? (float)evt.Intercepted / (evt.Intercepted + evt.Hits) * 100
                : 0f;

            // Functional wave_ended (drives AI news / chronicle) stays minimal and untouched.
            Record(Threat.WaveEnded, new ThreatWaveEndedData
            {
                WaveNumber = evt.WaveNumber,
                Intercepted = evt.Intercepted,
                Hits = evt.Hits,
                ShotsFired = evt.ShotsFired,
                Casualties = evt.Casualties,
                DamageCost = evt.DamageCost,
                SuccessRate = (float)Math.Round(successRate, 1)
            });

            // Separate developer-balance breakdown (drone/ballistic split, ammo economy). Per-AA-type
            // kill attribution is NOT tracked anywhere in the fire-control path — wiring it would mean
            // recording which launcher fired the lethal round, an invasive fire-control change. Left at 0
            // (the contract documents 0 = not-yet-tracked) until that attribution is designed separately.
            Record(Balance.WaveResult, new BalanceWaveResultData
            {
                WaveNumber = evt.WaveNumber,
                DroneIntercepted = evt.DroneIntercepted,
                DroneHits = evt.DroneHits,
                BallisticIntercepted = evt.BallisticIntercepted,
                BallisticHits = evt.BallisticHits,
                KillsHeritage = 0,
                KillsBofors = 0,
                KillsGepard = 0,
                KillsPatriot = 0,
                RoundsConsumed = evt.RoundsConsumed,
                MissilesConsumed = evt.MissilesConsumed
            });
        }

        private void OnThreatImpact(ThreatImpactEvent evt)
            => Record(Threat.Impact, evt.IsBallistic ? s_ThreatImpactBallistic : s_ThreatImpactDrone);

        private void OnThreatIntercept(ThreatInterceptEvent evt)
            => Record(Threat.Intercept, evt.IsBallistic ? s_ThreatInterceptBallistic : s_ThreatInterceptDrone);

        private void OnAAResupplyEvent(AAResupplyEvent evt)
        {
            Record(AaResupply.Result, new AaResupplyResultData
            {
                Result = evt.Result.ToString().ToSnakeCase(),
                Rounds = evt.Rounds,
                Needed = evt.Needed,
                Cost = evt.Cost
            });
        }

        private void OnBuildingDamaged(BuildingDamagedEvent evt)
        {
            Record(Building.Damaged, new BuildingDamagedData
            {
                BuildingIndex = evt.BuildingIndex,
                DamageAmount = evt.DamageAmount
            });
        }

        private void OnFirstStrikeCascade(FirstStrikeCascadeEvent evt)
        {
            Record(Scenario.FirstStrikeCascade, new ScenarioFirstStrikeCascadeData
            {
                PlannedHits = evt.PlannedHits
            });
        }

        private void OnInfrastructureCollapse(InfrastructureCollapseEvent evt)
        {
            Record(Scenario.InfraCollapse, new ScenarioInfraCollapseData
            {
                RefugeeCount = evt.RefugeeCount,
                OriginalPopulation = evt.OriginalPopulation,
                PopulationRatio = (float)Math.Round(evt.PopulationRatio, 2)
            });
        }

        private void OnObstacleIndexOob(ObstacleIndexOobEvent evt)
        {
            Record(Diagnostics.ObstacleIndexOob, new DiagnosticsObstacleIndexOobData
            {
                BuildingIdx = evt.BuildingIdx,
                BuildingsLength = evt.BuildingsLength,
                GridCount = evt.GridCount,
                OccurrenceCount = evt.OccurrenceCount
            });
        }

        private void OnSpatialIndexOob(SpatialIndexOobEvent evt)
        {
            Record(Diagnostics.SpatialIndexOob, new DiagnosticsSpatialIndexOobData
            {
                BuildingIdx = evt.BuildingIdx,
                PositionsLength = evt.PositionsLength,
                HashCount = evt.HashCount,
                OccurrenceCount = evt.OccurrenceCount
            });
        }
    }
}
