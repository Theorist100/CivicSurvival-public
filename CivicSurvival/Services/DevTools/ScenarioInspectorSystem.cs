#if DEBUG
using System;
using Unity.Entities;
using Colossal.UI.Binding;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.Refugees;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.Domain.Diplomacy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Domains.ShadowEconomy.Systems;
using CivicSurvival.Domains.AirDefense.Systems;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// DEBUG ONLY: Read-only display of all scenario state singletons for the DevTools panel.
    /// No writes. Pure observation.
    /// </summary>
    [ActIndependent]
    public partial class ScenarioInspectorSystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("ScenarioInspector");

        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_GridStressQuery;
        private EntityQuery m_ExodusQuery;
        private EntityQuery m_ShockQuery;
        private EntityQuery m_CognitiveQuery;
        private EntityQuery m_TelemarathonQuery;
        private EntityQuery m_ReputationQuery;
        private EntityQuery m_CountermeasuresQuery;
        private EntityQuery m_MobilizationQuery;
        private EntityQuery m_ShadowTradeQuery;
        private BufferLookup<CognitiveIntegrityBuffer> m_CogIntegrityBufferLookup;
        private ShadowWalletSystem? m_ShadowWallet;

        // Shadow Economy / Act
        private ProfiledBinding<string> m_CurrentAct = null!;
        private ProfiledBinding<int> m_WarDay = null!;
        private ProfiledBinding<int> m_ShadowBalance = null!;
        private ProfiledBinding<int> m_ShadowDailyIncome = null!;
        private ProfiledBinding<bool> m_GridWarfareUnlocked = null!;

        // Scenario state
        private ProfiledBinding<int> m_ScWaveNumber = null!;
        private ProfiledBinding<int> m_ScWaveInProgress = null!;
        private ProfiledBinding<int> m_ScWavePhase = null!;
        private ProfiledBinding<string> m_ScWavePhaseName = null!;
        private ProfiledBinding<int> m_ScEnemyStance = null!;
        private ProfiledBinding<string> m_ScEnemyStanceName = null!;
        private ProfiledBinding<float> m_ScEnemyPressure = null!;
        private ProfiledBinding<int> m_ScStancePhase = null!;
        private ProfiledBinding<string> m_ScStancePhaseName = null!;
        private ProfiledBinding<int> m_ScGridCollapsed = null!;
        private ProfiledBinding<float> m_ScGridStressHours = null!;
        private ProfiledBinding<float> m_ScGridRecoveryHours = null!;
        private ProfiledBinding<int> m_ScGridZone = null!;
        private ProfiledBinding<string> m_ScGridZoneName = null!;
        private ProfiledBinding<float> m_ScExodusRatePercentPerDay = null!;
        private ProfiledBinding<int> m_ScTotalFled = null!;
        private ProfiledBinding<int> m_ScExodusActive = null!;
        private ProfiledBinding<float> m_ScShockLevel = null!;
        private ProfiledBinding<int> m_ScShockTier = null!;
        private ProfiledBinding<string> m_ScShockTierName = null!;
        private ProfiledBinding<float> m_ScInfectionRate = null!;
        private ProfiledBinding<float> m_ScCityIntegrity = null!;
        private ProfiledBinding<float> m_ScMediaTrust = null!;
        private ProfiledBinding<int> m_ScTelemarathonActive = null!;
        private ProfiledBinding<float> m_ScTrustLevel = null!;
        private ProfiledBinding<float> m_ScCorruptionHeat = null!;
        private ProfiledBinding<float> m_ScCorruptionScore = null!;
        private ProfiledBinding<float> m_ScMoraleFactor = null!;
        private ProfiledBinding<int> m_ScAaAmmo = null!;

        protected override int UpdateInterval => 60;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadOnly<EnemyState>());
            m_GridStressQuery = GetEntityQuery(ComponentType.ReadOnly<GridStressData>());
            m_ExodusQuery = GetEntityQuery(ComponentType.ReadOnly<ExodusStateSingleton>());
            m_ShockQuery = GetEntityQuery(ComponentType.ReadOnly<ShockStateSingleton>());
            m_CognitiveQuery = GetEntityQuery(ComponentType.ReadOnly<CognitiveState>());
            m_TelemarathonQuery = GetEntityQuery(ComponentType.ReadOnly<TelemarathonRuntimeState>());
            m_ReputationQuery = GetEntityQuery(ComponentType.ReadOnly<ReputationStateSingleton>());
            m_CountermeasuresQuery = GetEntityQuery(ComponentType.ReadOnly<CountermeasuresCoreFsm>());
            m_MobilizationQuery = GetEntityQuery(ComponentType.ReadOnly<MobilizationStateSingleton>());
            m_ShadowTradeQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowExportState>());
            m_CogIntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);

            CreateBindings();
        }

        private void CreateBindings()
        {
            m_CurrentAct = new ProfiledBinding<string>(Group, Debug_CurrentAct, "PreWar");
            m_WarDay = new ProfiledBinding<int>(Group, Debug_WarDay, 0);
            m_ShadowBalance = new ProfiledBinding<int>(Group, Debug_ShadowBalance, 0);
            m_ShadowDailyIncome = new ProfiledBinding<int>(Group, Debug_ShadowDailyIncome, 0);
            m_GridWarfareUnlocked = new ProfiledBinding<bool>(Group, Debug_GridWarfareUnlocked, false);
            m_ScWaveNumber = new ProfiledBinding<int>(Group, Debug_WaveNumber, 0);
            m_ScWaveInProgress = new ProfiledBinding<int>(Group, Debug_WaveInProgress, 0);
            m_ScWavePhase = new ProfiledBinding<int>(Group, Debug_WavePhase, 0);
            m_ScWavePhaseName = new ProfiledBinding<string>(Group, Debug_WavePhaseName, "Calm");
            m_ScEnemyStance = new ProfiledBinding<int>(Group, Debug_EnemyStance, 0);
            m_ScEnemyStanceName = new ProfiledBinding<string>(Group, Debug_EnemyStanceName, "IronDome");
            m_ScEnemyPressure = new ProfiledBinding<float>(Group, Debug_EnemyPressure, 0f);
            m_ScStancePhase = new ProfiledBinding<int>(Group, Debug_StancePhase, 0);
            m_ScStancePhaseName = new ProfiledBinding<string>(Group, Debug_StancePhaseName, "Active");
            m_ScGridCollapsed = new ProfiledBinding<int>(Group, Debug_GridCollapsed, 0);
            m_ScGridStressHours = new ProfiledBinding<float>(Group, Debug_GridStressHours, 0f);
            m_ScGridRecoveryHours = new ProfiledBinding<float>(Group, Debug_GridRecoveryHours, 0f);
            m_ScGridZone = new ProfiledBinding<int>(Group, Debug_GridZone, 0);
            m_ScGridZoneName = new ProfiledBinding<string>(Group, Debug_GridZoneName, "Normal");
            m_ScExodusRatePercentPerDay = new ProfiledBinding<float>(Group, Debug_ExodusRatePercentPerDay, 0f);
            m_ScTotalFled = new ProfiledBinding<int>(Group, Debug_TotalFled, 0);
            m_ScExodusActive = new ProfiledBinding<int>(Group, Debug_ExodusActive, 0);
            m_ScShockLevel = new ProfiledBinding<float>(Group, Debug_ShockLevel, 0f);
            m_ScShockTier = new ProfiledBinding<int>(Group, Debug_ShockTier, 0);
            m_ScShockTierName = new ProfiledBinding<string>(Group, Debug_ShockTierName, "DeepConcern");
            m_ScInfectionRate = new ProfiledBinding<float>(Group, Debug_InfectionRate, 0f);
            m_ScCityIntegrity = new ProfiledBinding<float>(Group, Debug_CityIntegrity, 1f);
            m_ScMediaTrust = new ProfiledBinding<float>(Group, Debug_MediaTrust, 0f);
            m_ScTelemarathonActive = new ProfiledBinding<int>(Group, Debug_TelemarathonActive, 0);
            m_ScTrustLevel = new ProfiledBinding<float>(Group, Debug_TrustLevel, 0f);
            m_ScCorruptionHeat = new ProfiledBinding<float>(Group, Debug_CorruptionHeat, 0f);
            m_ScCorruptionScore = new ProfiledBinding<float>(Group, Debug_CorruptionScore, 0f);
            m_ScMoraleFactor = new ProfiledBinding<float>(Group, Debug_MoraleFactor, 1f);
            m_ScAaAmmo = new ProfiledBinding<int>(Group, Debug_AaAmmo, 0);

            AddBinding(m_CurrentAct.Binding);
            AddBinding(m_WarDay.Binding);
            AddBinding(m_ShadowBalance.Binding);
            AddBinding(m_ShadowDailyIncome.Binding);
            AddBinding(m_GridWarfareUnlocked.Binding);
            AddBinding(m_ScWaveNumber.Binding);
            AddBinding(m_ScWaveInProgress.Binding);
            AddBinding(m_ScWavePhase.Binding);
            AddBinding(m_ScWavePhaseName.Binding);
            AddBinding(m_ScEnemyStance.Binding);
            AddBinding(m_ScEnemyStanceName.Binding);
            AddBinding(m_ScEnemyPressure.Binding);
            AddBinding(m_ScStancePhase.Binding);
            AddBinding(m_ScStancePhaseName.Binding);
            AddBinding(m_ScGridCollapsed.Binding);
            AddBinding(m_ScGridStressHours.Binding);
            AddBinding(m_ScGridRecoveryHours.Binding);
            AddBinding(m_ScGridZone.Binding);
            AddBinding(m_ScGridZoneName.Binding);
            AddBinding(m_ScExodusRatePercentPerDay.Binding);
            AddBinding(m_ScTotalFled.Binding);
            AddBinding(m_ScExodusActive.Binding);
            AddBinding(m_ScShockLevel.Binding);
            AddBinding(m_ScShockTier.Binding);
            AddBinding(m_ScShockTierName.Binding);
            AddBinding(m_ScInfectionRate.Binding);
            AddBinding(m_ScCityIntegrity.Binding);
            AddBinding(m_ScMediaTrust.Binding);
            AddBinding(m_ScTelemarathonActive.Binding);
            AddBinding(m_ScTrustLevel.Binding);
            AddBinding(m_ScCorruptionHeat.Binding);
            AddBinding(m_ScCorruptionScore.Binding);
            AddBinding(m_ScMoraleFactor.Binding);
            AddBinding(m_ScAaAmmo.Binding);
        }

        protected override void OnThrottledUpdate()
        {
            try
            {
                if (World == null || !World.IsCreated) return;

                UpdateActAndShadow();
                UpdateScenarioState();
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[DEBUG] {nameof(ScenarioInspectorSystem)}: {ex}");
            }
        }

        private void UpdateActAndShadow()
        {
            m_ShadowWallet = World.GetExistingSystemManaged<ShadowWalletSystem>();

            Act currentAct = Act.PreWar;
            int warDay = Core.Systems.GameTimeSystem.Instance?.Current.WarDay ?? -1;
            if (SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
            {
                currentAct = actSingleton.CurrentAct;
            }
            m_CurrentAct.Update(EnumName<Act>.Get(currentAct));
            m_WarDay.Update(warDay);
            m_GridWarfareUnlocked.Update(currentAct >= Act.Adaptation);
            m_ShadowBalance.Update((int)(m_ShadowWallet != null ? m_ShadowWallet.TotalBalance : 0));

            int dailyIncome = 0;
            if (m_ShadowTradeQuery.TryGetSingleton<ShadowExportState>(out var tradeState))
                dailyIncome = tradeState.ExportDailyIncome;
            m_ShadowDailyIncome.Update(dailyIncome);
        }

        private void UpdateScenarioState()
        {
            m_CogIntegrityBufferLookup.Update(this);

            if (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var wave))
            {
                m_ScWaveNumber.Update(wave.WaveNumber);
                m_ScWaveInProgress.Update(wave.IsUnderAttack ? 1 : 0);
                m_ScWavePhase.Update((int)wave.CurrentPhase);
                m_ScWavePhaseName.Update(EnumName<GamePhase>.Get(wave.CurrentPhase));
            }

            if (m_EnemyStateQuery.TryGetSingleton<EnemyState>(out var enemy))
            {
                // Mirror-enemy model: three axes (physical/digital/social), no RPS stance.
                // The legacy stance/pressure/phase debug slots now carry the three axes:
                // pressure→physical, stance(int+name)→digital, phase(int+name)→social.
                m_ScEnemyPressure.Update(enemy.PhysicalAxis);
                m_ScEnemyStance.Update((int)enemy.DigitalAxis);
                m_ScEnemyStanceName.Update($"digital {enemy.DigitalAxis:F0}%");
                m_ScStancePhase.Update((int)enemy.SocialAxis);
                m_ScStancePhaseName.Update($"social {enemy.SocialAxis:F0}%");
            }

            if (m_GridStressQuery.TryGetSingleton<GridStressData>(out var grid))
            {
                m_ScGridCollapsed.Update(grid.IsCollapsed ? 1 : 0);
                m_ScGridStressHours.Update(Math.Max(0f, grid.StressHours));
                m_ScGridRecoveryHours.Update(grid.RecoveryHoursRemaining);
                m_ScGridZone.Update((int)grid.Zone);
                m_ScGridZoneName.Update(EnumName<GridStressZone>.Get(grid.Zone));
            }

            if (m_ExodusQuery.TryGetSingleton<ExodusStateSingleton>(out var exodus))
            {
                m_ScExodusRatePercentPerDay.Update(exodus.ExodusRatePercentPerDay);
                m_ScTotalFled.Update(exodus.TotalExodus);
                m_ScExodusActive.Update(exodus.IsExodusActive ? 1 : 0);
            }

            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock))
            {
                m_ScShockLevel.Update(shock.ShockLevel);
                m_ScShockTier.Update((int)shock.CurrentTier);
                m_ScShockTierName.Update(EnumName<AidTier>.Get(shock.CurrentTier));
            }

            if (m_CognitiveQuery.TryGetSingleton<CognitiveState>(out var cog))
            {
                var heroState = SystemAPI.TryGetSingleton<HeroDeploymentState>(out var hs)
                    ? hs : HeroDeploymentState.Default;
                m_ScInfectionRate.Update(CognitiveRates.EffectiveInfectionRate(cog, heroState));
                m_ScCityIntegrity.Update(GetCityIntegrity());
            }

            if (m_TelemarathonQuery.TryGetSingleton<TelemarathonRuntimeState>(out var tele))
            {
                m_ScMediaTrust.Update(tele.Trust);
                m_ScTelemarathonActive.Update(tele.IsActive ? 1 : 0);
            }

            if (m_ReputationQuery.TryGetSingleton<ReputationStateSingleton>(out var rep))
                m_ScTrustLevel.Update(rep.TrustLevel);

            if (m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var cm))
            {
                m_ScCorruptionHeat.Update(cm.Heat);
                m_ScCorruptionScore.Update(cm.CorruptionScore);
            }

            if (m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var mobilization))
                m_ScMoraleFactor.Update(mobilization.MoraleFactor);

            var airDefenseState = FeatureRegistry.IsInitialized
                ? FeatureRegistry.Instance.Query<AirDefenseStateSystem>()
                : null;
            if (airDefenseState == null)
                m_ScAaAmmo.Update(0);
            else
                m_ScAaAmmo.Update(airDefenseState.GetUiStatsSnapshot().AaAmmo);
        }

        private float GetCityIntegrity()
        {
            if (!m_CognitiveQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return 1f;

            if (!m_CogIntegrityBufferLookup.TryGetBuffer(stateEntity, out var buffer) || buffer.Length == 0)
                return 1f;

            float totalIntegrity = 0f;
            for (int i = 0; i < buffer.Length; i++)
                totalIntegrity += buffer[i].Integrity;

            return totalIntegrity / buffer.Length;
        }
    }
}
#endif
