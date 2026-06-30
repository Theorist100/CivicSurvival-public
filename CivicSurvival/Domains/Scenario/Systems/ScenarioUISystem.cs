using System;
using Game;
using Game.Simulation;
using Unity.Entities;
using Colossal.UI.Binding;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Scenario.Systems;
using CivicSurvival.Core.Systems.Base;
using Game.City;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.UI.DomainState;
using UnityEntity = Unity.Entities.Entity;

namespace CivicSurvival.Domains.Scenario.UI
{
    /// <summary>
    /// UI bindings for scenario state and statistics.
    /// Thin layer that exposes data to React UI.
    /// Reads from ScenarioStateMachine, ScenarioStatisticsSystem.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.OneMoreYear)]
    [HandlesRequestKind(RequestKind.EndlessMode)]
    public partial class ScenarioUISystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("ScenarioUISystem");

        // Dependencies (initialized in OnStartRunning)
        private ScenarioStateMachine m_StateMachine = null!;
        private ScenarioStatisticsSystem m_Statistics = null!;
        private ScenarioMilestonesSystem m_Milestones = null!;
        private CitySystem m_CitySystem = null!;

        // Core state bindings (initialized in OnCreate)
        private ProfiledBinding<int> m_ScenarioTypeBinding = null!;
        private ProfiledBinding<int> m_CurrentActBinding = null!;
        private ProfiledBinding<int> m_TotalRefugeesBinding = null!;
        private ProfiledBinding<float> m_PopulationPercentBinding = null!;

        // Statistics bindings (initialized in OnCreate)
        private ProfiledBinding<int> m_WavesDefendedBinding = null!;
        private ProfiledBinding<int> m_MissilesInterceptedBinding = null!;
        private ProfiledBinding<int> m_BlackoutRecoveriesBinding = null!;
        private ProfiledBinding<int> m_BuildingsDamagedBinding = null!;

        private ProfiledBinding<string> m_OneMoreYearRequestBinding = null!;
        private ProfiledBinding<string> m_EndlessModeRequestBinding = null!;

        // Defeat bindings
        private ProfiledBinding<int> m_DefeatCauseBinding = null!;
        private ProfiledBinding<int> m_DaysSurvivedBinding = null!;
        // S7-05: Suppress defeat modal after user dismisses (reset on new GameOverEvent)
        private bool m_GameOverActive;
        private DefeatCause m_GameOverCause;
        private int m_GameOverDaysSurvived;
        private float m_LastGoodPopulationPercent = 1f;
        private bool m_WarFatigueModalRequested;
        private bool m_VictoryModalRequested;
        private bool m_DefeatModalRequested;

        // Vanilla population lookup (RO — no sync point on Citizen archetype)
        private ComponentLookup<Population> m_PopulationLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            // Initialize bindings
            m_ScenarioTypeBinding = new ProfiledBinding<int>(Group, Core.UI.B.ScenarioType, (int)CivicSurvival.Core.Types.ScenarioType.None);
            m_CurrentActBinding = new ProfiledBinding<int>(Group, CurrentAct, (int)Act.PreWar);
            m_TotalRefugeesBinding = new ProfiledBinding<int>(Group, TotalRefugees, 0);
            m_PopulationPercentBinding = new ProfiledBinding<float>(Group, PopulationPercent, 1f);
            m_WavesDefendedBinding = new ProfiledBinding<int>(Group, WavesDefended, 0);
            m_MissilesInterceptedBinding = new ProfiledBinding<int>(Group, MissilesIntercepted, 0);
            m_BlackoutRecoveriesBinding = new ProfiledBinding<int>(Group, BlackoutRecoveries, 0);
            m_BuildingsDamagedBinding = new ProfiledBinding<int>(Group, BuildingsDamaged, 0);
            m_OneMoreYearRequestBinding = new ProfiledBinding<string>(Group, OneMoreYearRequest, RequestResultBridge.Get(RequestResultBridge.OneMoreYear).ToJson());
            m_EndlessModeRequestBinding = new ProfiledBinding<string>(Group, EndlessModeRequest, RequestResultBridge.Get(RequestResultBridge.EndlessMode).ToJson());
            m_DefeatCauseBinding = new ProfiledBinding<int>(Group, Core.UI.B.DefeatCause, 0);
            m_DaysSurvivedBinding = new ProfiledBinding<int>(Group, DaysSurvived, 0);

            AddBinding(m_ScenarioTypeBinding.Binding);
            AddBinding(m_CurrentActBinding.Binding);
            AddBinding(m_TotalRefugeesBinding.Binding);
            AddBinding(m_PopulationPercentBinding.Binding);
            AddBinding(m_WavesDefendedBinding.Binding);
            AddBinding(m_MissilesInterceptedBinding.Binding);
            AddBinding(m_BlackoutRecoveriesBinding.Binding);
            AddBinding(m_BuildingsDamagedBinding.Binding);
            AddBinding(m_OneMoreYearRequestBinding.Binding);
            AddBinding(m_EndlessModeRequestBinding.Binding);
            AddBinding(m_DefeatCauseBinding.Binding);
            AddBinding(m_DaysSurvivedBinding.Binding);

            // Modal dismiss triggers
            // Lifecycle wrap: scenario triggers mutate gameplay state machine.
            AddBinding(new TriggerBinding(Group, DismissWarFatigue,
                CivicGameLifecycle.GameplayOnly(OnDismissWarFatigue)));
            AddBinding(new TriggerBinding(Group, DismissDefeat,
                CivicGameLifecycle.GameplayOnly(OnDismissDefeat)));
            AddBinding(new TriggerBinding(Group, DismissGridCollapse,
                CivicGameLifecycle.GameplayOnly(OnDismissGridCollapse)));
            AddBinding(new TriggerBinding(Group, DismissGridCritical,
                CivicGameLifecycle.GameplayOnly(OnDismissGridCritical)));
            AddBinding(new TriggerBinding(Group, DismissModLoadFailure,
                CivicGameLifecycle.GameplayOnly(OnDismissModLoadFailure)));
            AddBinding(new TriggerBinding(Group, OneMoreYear,
                CivicGameLifecycle.GameplayOnly(() =>
                    TriggerDispatch.Invoke(RequestResultBridge.OneMoreYear, OneMoreYear, () => InvokeVictoryChoice(OnOneMoreYear)))));
            AddBinding(new TriggerBinding(Group, EndlessMode,
                CivicGameLifecycle.GameplayOnly(() =>
                    TriggerDispatch.Invoke(RequestResultBridge.EndlessMode, EndlessMode, () => InvokeVictoryChoice(OnEndlessMode)))));

            m_PopulationLookup = GetComponentLookup<Population>(true);

            SubscribeRequired<GameOverEvent>(OnGameOver);
            SubscribeRequired<ScenarioTypeDetectedEvent>(OnScenarioLoaded);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateMachine ??= FeatureRegistry.Instance.Require<ScenarioStateMachine>();
            m_Statistics ??= FeatureRegistry.Instance.Require<ScenarioStatisticsSystem>();
            m_Milestones ??= FeatureRegistry.Instance.Require<ScenarioMilestonesSystem>();
        }

        protected override void OnThrottledUpdate()
        {
            // Read state from systems
            var state = m_StateMachine.State;

            m_ScenarioTypeBinding.Update((int)state.Type);
            m_CurrentActBinding.Update((int)state.CurrentAct);
            m_TotalRefugeesBinding.Update(state.RefugeesReceived);
            // Read vanilla Population via canonical city entity (more robust than TryGetSingletonEntity)
            m_PopulationLookup.Update(this);
            var city = m_CitySystem?.City ?? UnityEntity.Null;
            bool hasPopulation = city != UnityEntity.Null && m_PopulationLookup.HasComponent(city);
            int currentPop = hasPopulation ? m_PopulationLookup[city].m_Population : 0;
            if (hasPopulation || state.PeakPopulation <= 0)
            {
                m_LastGoodPopulationPercent = state.PeakPopulation > 0
                    ? System.Math.Min((float)currentPop / state.PeakPopulation, 1f)
                    : 1f;
            }
            m_PopulationPercentBinding.Update(m_LastGoodPopulationPercent);

            bool statisticsReady = m_Statistics.Enabled;
            m_WavesDefendedBinding.Update(statisticsReady ? m_Statistics.TotalWavesDefended : 0);
            m_MissilesInterceptedBinding.Update(statisticsReady ? m_Statistics.TotalMissilesIntercepted : 0);
            m_BlackoutRecoveriesBinding.Update(statisticsReady ? m_Statistics.TotalBlackoutRecoveries : 0);
            m_BuildingsDamagedBinding.Update(statisticsReady ? m_Statistics.TotalBuildingsDamaged : 0);

            bool milestonesReady = m_Milestones.Enabled;
            UpdateCoordinatedModal("WarFatigue", milestonesReady && m_Milestones.WarFatigueShown && !m_Milestones.WarFatigueDismissed, ref m_WarFatigueModalRequested);
            UpdateCoordinatedModal("Victory", milestonesReady && m_Milestones.VictoryShown && !m_Milestones.VictoryDismissed, ref m_VictoryModalRequested);
            m_OneMoreYearRequestBinding.Update(RequestResultBridge.Get(RequestResultBridge.OneMoreYear).ToJson());
            m_EndlessModeRequestBinding.Update(RequestResultBridge.Get(RequestResultBridge.EndlessMode).ToJson());

            bool defeatActive = m_StateMachine.IsDefeated || m_GameOverActive;
            bool defeatDismissed = GetModalReader().IsDefeatDismissed;
            UpdateCoordinatedModal("Defeat", defeatActive && !defeatDismissed, ref m_DefeatModalRequested);
            m_DefeatCauseBinding.Update((int)(m_GameOverActive ? m_GameOverCause : m_StateMachine.DefeatCause));
            m_DaysSurvivedBinding.Update(m_GameOverActive ? m_GameOverDaysSurvived : m_StateMachine.WarDay);
        }

#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
        private void OnDismissWarFatigue()
        {
            m_Milestones.DismissWarFatigue();
            m_WarFatigueModalRequested = false;
            ModalCoordinator.Instance.Dismiss("WarFatigue");
            Log.Info("War Fatigue modal dismissed");
        }

        private void OnScenarioLoaded(ScenarioTypeDetectedEvent evt)
        {
            m_GameOverActive = false;
            if (!m_StateMachine.IsDefeated)
                GetModalMutator().ClearDefeatDismissed();
            m_WarFatigueModalRequested = false;
            m_VictoryModalRequested = false;
            m_DefeatModalRequested = false;
        }

        private void OnGameOver(GameOverEvent evt)
        {
            if (evt.Cause == CivicSurvival.Core.Types.DefeatCause.Arrested)
            {
                Log.Info("GameOverEvent Arrested received; Countermeasures owns the arrested modal");
                return;
            }

            m_GameOverActive = true;
            m_GameOverCause = evt.Cause;
            m_GameOverDaysSurvived = evt.DaysSurvived;
            GetModalMutator().ClearDefeatDismissed();
            _ = ModalCoordinator.Instance.TryShow("Defeat");
            m_DefeatModalRequested = true;
            m_DefeatCauseBinding.Update((int)evt.Cause);
            m_DaysSurvivedBinding.Update(evt.DaysSurvived);
            Log.Info($"GameOverEvent received: {evt.Cause}, days survived: {evt.DaysSurvived}");
        }

        private void OnDismissDefeat()
        {
            GetModalMutator().MarkDefeatDismissed();
            m_GameOverActive = false;
            m_DefeatModalRequested = false;
            ModalCoordinator.Instance.Dismiss("Defeat");
            Log.Info("Defeat modal dismissed");
        }

        // Mod-load-failure modal is shown by CivicPrefabInitSystem (Core bootstrap) when the
        // core threat .cok are genuinely absent; dismiss lives here with the other modal triggers.
        private void OnDismissModLoadFailure()
        {
            ModalCoordinator.Instance.Dismiss("ModLoadFailure");
            Log.Info("Mod load failure modal dismissed");
        }

        // Grid-collapse modal is shown by GridStressSystem (Engineering) via the Core
        // ModalCoordinator; dismiss lives here alongside the other modal dismiss triggers.
        private void OnDismissGridCollapse()
        {
            ModalCoordinator.Instance.Dismiss("GridCollapse");
            Log.Info("Grid collapse modal dismissed");
        }

        // Pre-collapse warning modal is shown by GridStressSystem (Engineering) via the
        // Core ModalCoordinator; dismiss lives here alongside the other modal triggers.
        private void OnDismissGridCritical()
        {
            ModalCoordinator.Instance.Dismiss("GridCritical");
            Log.Info("Grid critical modal dismissed");
        }

        private TriggerOutcome OnOneMoreYear()
        {
            m_Milestones.SetPostVictoryMode(PostVictoryMode.OneMoreYear);
            m_VictoryModalRequested = false;
            ModalCoordinator.Instance.Dismiss("Victory");
            Log.Info("Player chose One More Year");
            return TriggerOutcome.SyncSuccess();
        }

        private TriggerOutcome OnEndlessMode()
        {
            m_Milestones.SetPostVictoryMode(PostVictoryMode.Endless);
            m_VictoryModalRequested = false;
            ModalCoordinator.Instance.Dismiss("Victory");
            Log.Info("Player chose Endless Mode");
            return TriggerOutcome.SyncSuccess();
        }

        private static TriggerOutcome InvokeVictoryChoice(Func<TriggerOutcome> handler)
        {
            if (ModalCoordinator.Instance.ActiveId != "Victory")
            {
                Log.Info("Ignoring victory choice trigger because Victory modal is not active");
                return TriggerOutcome.Reject(ReasonIds.VictoryNotActive);
            }

            return handler();
        }

        /// <summary>
        /// Gate a modal binding through the coordinator.
        /// Shows only if coordinator slot is free; dismisses when no longer wanted.
        /// </summary>
        private static void UpdateCoordinatedModal(string modalId, bool wantsToShow, ref bool modalRequested)
        {
            if (wantsToShow)
            {
                if (!modalRequested)
                {
                    _ = ModalCoordinator.Instance.TryShow(modalId);
                    modalRequested = true;
                }
            }
            else if (modalRequested)
            {
                modalRequested = false;
                ModalCoordinator.Instance.Dismiss(modalId);
            }
        }
#pragma warning restore CIVIC098

        private IScenarioModalReader GetModalReader()
        {
            return m_StateMachine;
        }

        private IScenarioModalMutator GetModalMutator()
        {
            return m_StateMachine;
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<GameOverEvent>(OnGameOver);
            UnsubscribeSafe<ScenarioTypeDetectedEvent>(OnScenarioLoaded);
            base.OnDestroy();
        }
    }
}
