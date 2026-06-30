#if DEBUG
using System;
using System.Collections.Generic;
using Unity.Entities;
using Colossal.Logging;
using Colossal.UI.Binding;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.Diplomacy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Domain.Refugees;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Domains.Engineering.Systems;
using CivicSurvival.Domains.Scenario.Systems;
using Unity.Mathematics;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// DEBUG ONLY: Write-only debug actions — triggers and presets that modify game state.
    /// No ValueBindings. All output goes through ScenarioInspectorSystem.
    /// </summary>
    [ActIndependent]
    public partial class ScenarioDebugActionSystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("ScenarioDebugAction");

        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_GridStressQuery;
        private EntityQuery m_ExodusQuery;
        private EntityQuery m_ShockQuery;
        private EntityQuery m_CognitiveQuery;
        private BufferLookup<CognitiveIntegrityBuffer> m_CogIntegrityBufferLookup;
        private IEventBus? m_EventBus;
        private IShockDebugMutator? m_ShockMutator;
        private IExodusDebugMutator? m_ExodusMutator;
        private ICountermeasuresDebugMutator? m_CountermeasuresMutator;
        private IReputationDebugMutator? m_ReputationMutator;
        private IEnemyDebugMutator? m_EnemyMutator;
        private IMobilizationDebugMutator? m_MobilizationMutator;
        private IEconomyDebugMutator? m_EconomyMutator;
        private ITerrainHeightReader m_TerrainHeightReader = null!;
        private readonly LinkedList<PendingDebugAction> m_PendingActions = new();
        private readonly object m_PendingActionsLock = new();
        private const int MAX_PENDING_ACTIONS = 32;
        private const int MAX_ACTIONS_PER_FRAME = 8;
        private readonly struct PendingDebugAction
        {
            public PendingDebugAction(string key, Action action)
            {
                Key = key;
                Action = action;
            }

            public string Key { get; }
            public Action Action { get; }
        }

        protected override int UpdateInterval => 60;

        private bool m_BindingsRegistered;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_GridStressQuery = GetEntityQuery(ComponentType.ReadWrite<GridStressData>());
            m_ExodusQuery = GetEntityQuery(ComponentType.ReadOnly<ExodusStateSingleton>());
            m_ShockQuery = GetEntityQuery(ComponentType.ReadOnly<ShockStateSingleton>());
            m_CognitiveQuery = GetEntityQuery(ComponentType.ReadOnly<CognitiveState>());
            m_CogIntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);
            m_EventBus = GetEventBus();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_TerrainHeightReader = ServiceRegistry.Instance.Require<ITerrainHeightReader>();
            if (m_BindingsRegistered) return;
            m_BindingsRegistered = true;

            // Bindings register here (not OnCreate) so the debug-mutator services they
            // resolve via Resolve*Mutator() are guaranteed registered by the time a
            // trigger actually fires — CIVIC403 safety.
            AddBinding(new TriggerBinding<int>(Group, Debug_SetAct, value => EnqueueDebugAction(Debug_SetAct, () => OnDebugSetAct(value))));
            AddBinding(new TriggerBinding(Group, DebugForceGridCollapse, () => EnqueueDebugAction(DebugForceGridCollapse, OnDebugForceGridCollapse)));
            AddBinding(new TriggerBinding(Group, DebugResetGridStress, () => EnqueueDebugAction(DebugResetGridStress, OnDebugResetGridStress)));
            AddBinding(new TriggerBinding<float>(Group, DebugSetStress, value => EnqueueDebugAction(DebugSetStress, () => OnDebugSetStress(value))));
            AddBinding(new TriggerBinding(Group, DebugForceWave, () => EnqueueDebugAction(DebugForceWave, OnDebugForceWave)));
            AddBinding(new TriggerBinding<float>(Group, DebugSetShock, value => EnqueueDebugAction(DebugSetShock, () => OnDebugSetShock(value))));
            AddBinding(new TriggerBinding<float>(Group, DebugSetCorruption, value => EnqueueDebugAction(DebugSetCorruption, () => OnDebugSetCorruption(value))));
            AddBinding(new TriggerBinding<float>(Group, DebugSetCityIntegrity, value => EnqueueDebugAction(DebugSetCityIntegrity, () => OnDebugSetCityIntegrity(value))));
            AddBinding(new TriggerBinding(Group, B.DebugToggleExodus, () => EnqueueDebugAction(B.DebugToggleExodus, OnDebugToggleExodus)));
            AddBinding(new TriggerBinding(Group, DebugForceDayChange, () => EnqueueDebugAction(DebugForceDayChange, OnDebugForceDayChange)));
            AddBinding(new TriggerBinding<float>(Group, DebugSetEnemyPressure, value => EnqueueDebugAction(DebugSetEnemyPressure, () => OnDebugSetEnemyPressure(value))));
            AddBinding(new TriggerBinding<float>(Group, DebugSetTrust, value => EnqueueDebugAction(DebugSetTrust, () => OnDebugSetTrust(value))));
            AddBinding(new TriggerBinding<float>(Group, DebugSetMoraleFactor, value => EnqueueDebugAction(DebugSetMoraleFactor, () => OnDebugSetMoraleFactor(value))));
            AddBinding(new TriggerBinding<int>(Group, DebugRunPreset, value => EnqueueDebugAction(DebugRunPreset, () => OnDebugRunPreset(value))));
            AddBinding(new TriggerBinding(Group, DebugTestExplosion, () => EnqueueDebugAction(DebugTestExplosion, OnDebugTestExplosion)));
        }

        protected override void OnThrottledUpdate()
        {
            int processed = 0;
            while (processed < MAX_ACTIONS_PER_FRAME)
            {
                PendingDebugAction pending;
                lock (m_PendingActionsLock)
                {
                    if (m_PendingActions.Count == 0)
                        return;

                    pending = m_PendingActions.First!.Value;
                    m_PendingActions.RemoveFirst();
                }

                pending.Action();
                processed++;
            }

            lock (m_PendingActionsLock)
            {
                if (m_PendingActions.Count > 0)
                    ForceNextUpdate();
            }
        }

        private void EnqueueDebugAction(string key, Action action)
        {
            bool shouldLogDrop = false;
            lock (m_PendingActionsLock)
            {
                for (var node = m_PendingActions.First; node != null; node = node.Next)
                {
                    if (node.Value.Key == key)
                    {
                        m_PendingActions.Remove(node);
                        break;
                    }
                }

                if (m_PendingActions.Count >= MAX_PENDING_ACTIONS)
                {
                    m_PendingActions.RemoveFirst();
                    shouldLogDrop = true;
                }
                m_PendingActions.AddLast(new PendingDebugAction(key, action));
            }
            if (shouldLogDrop)
                Log.Warn("[DEBUG] Pending action queue full; dropped oldest action");
            ForceNextUpdate();
        }

        // ============================================================================
        // TRIGGER HANDLERS
        // ============================================================================

        private void OnDebugSetAct(int actIndex)
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                if (actIndex < 0 || actIndex > 4) return;
                var ssm = World.GetExistingSystemManaged<ScenarioStateMachine>();
                if (ssm == null) return;
                var newAct = (Act)actIndex;
                Log.Info($"[DEBUG] Setting Act to: {newAct}");
                if (newAct != Act.PreWar)
                    GameTimeSystem.Instance?.DebugEnsureWarStarted();
                ssm.TransitionToAct(newAct);
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetAct failed: {ex}"); }
        }

        private void OnDebugForceGridCollapse()
        {
            try
            {
                ResolveGridStressSystem()?.DebugForceCollapse("scenario_button");
                Log.Info("[DEBUG] Forced grid collapse");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugForceGridCollapse failed: {ex}"); }
        }

        private void OnDebugResetGridStress()
        {
            try
            {
                ResolveGridStressSystem()?.DebugResetStress("scenario_button");
                Log.Info("[DEBUG] Reset grid stress to default");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugResetGridStress failed: {ex}"); }
        }

        private void OnDebugSetStress(float value)
        {
            try
            {
                ResolveGridStressSystem()?.DebugSetStressHours(value, "scenario_slider");
                Log.Info($"[DEBUG] Set grid stress hours to {value:F2}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetStress failed: {ex}"); }
        }

        private void OnDebugForceWave()
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                var eventBus = GetEventBus();
                if (eventBus == null) return;
                GameTimeSystem.Instance?.DebugEnsureWarStarted();
                int waveNum = 99;
                if (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws))
                    waveNum = ws.WaveNumber + 1;
                eventBus.SafePublish(
                    new SpawnWaveRequestEvent(10, WaveNumber: waveNum, WaveType.MassiveStrike),
                    "ScenarioDebugActionSystem.ForceWave"
                );
                Log.Info($"[DEBUG] Force wave #{waveNum}: 10 drones");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugForceWave failed: {ex}"); }
        }

        private void OnDebugSetShock(float value)
        {
            try
            {
                ResolveShockMutator()?.DebugSetShockLevel(value, "scenario_slider");
                Log.Info($"[DEBUG] Set shock level to {value:F1}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetShock failed: {ex}"); }
        }

        private void OnDebugSetCorruption(float value)
        {
            try
            {
                ResolveCountermeasuresMutator()?.DebugSetCorruption(value, "scenario_slider");
                Log.Info($"[DEBUG] Set corruption to {value:F1}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetCorruption failed: {ex}"); }
        }

        private void OnDebugSetCityIntegrity(float value)
        {
            try
            {
                if (!FeatureRegistry.IsInitialized) return;
                var cognitive = FeatureRegistry.Instance.Query<CognitiveStateSystem>();
                if (cognitive == null || !cognitive.DebugOverrideIntegrity(value))
                {
                    Log.Warn("[DEBUG] CognitiveStateSystem unavailable — city integrity override ignored");
                    return;
                }
                Log.Info($"[DEBUG] Set city integrity to {value:F2}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetCityIntegrity failed: {ex}"); }
        }

        private void OnDebugToggleExodus()
        {
            try
            {
                var exodus = ResolveExodusMutator();
                if (exodus == null) return;
                bool active = !exodus.DebugIsExodusActive;
                exodus.DebugSetExodusActive(active, "scenario_toggle");
                Log.Info($"[DEBUG] Toggled exodus active = {active}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugToggleExodus failed: {ex}"); }
        }

        private void OnDebugForceDayChange()
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                if (GameTimeSystem.Instance == null)
                {
                    Log.Warn("[DEBUG] ForceDayChange skipped: GameTimeSystem unavailable");
                    return;
                }
                GameTimeSystem.Instance.DebugAdvanceDay();
                int day = GameTimeSystem.TryGetDay(out var currentDay) ? currentDay : -1;
                Log.Info($"[DEBUG] Forced day change → day {day}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugForceDayChange failed: {ex}"); }
        }

        private void OnDebugSetEnemyPressure(float value)
        {
            try
            {
                ResolveEnemyMutator()?.DebugSetPressure(value, "scenario_slider");
                Log.Info($"[DEBUG] Set enemy pressure to {value:F1}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetEnemyPressure failed: {ex}"); }
        }

        private void OnDebugSetTrust(float value)
        {
            try
            {
                ResolveReputationMutator()?.DebugSetTrust(value, "scenario_slider");
                Log.Info($"[DEBUG] Set trust level to {value:F1}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetTrust failed: {ex}"); }
        }

        private void OnDebugSetMoraleFactor(float value)
        {
            try
            {
                ResolveMobilizationMutator()?.DebugSetMoraleFactor(value, "scenario_slider");
                Log.Info($"[DEBUG] Set morale factor to {value:F2}");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugSetMoraleFactor failed: {ex}"); }
        }

        private void OnDebugTestExplosion()
        {
            try
            {
                var vfx = World.GetExistingSystemManaged<VanillaVfxSystem>();
                if (vfx == null || !vfx.IsReady)
                {
                    Log.Warn("[DEBUG] VanillaVfxSystem not ready");
                    return;
                }

                var cam = UnityEngine.Camera.main;
                if (cam == null)
                {
                    Log.Warn("[DEBUG] No camera");
                    return;
                }

                // 100m in front of camera, snapped to terrain when available.
                var camPos = cam.transform.position;
                var forward = cam.transform.forward;
                var pos = new float3(
                    camPos.x + forward.x * 100f,
                    camPos.y,
                    camPos.z + forward.z * 100f);
                if (!m_TerrainHeightReader.TrySampleHeight(pos, out var terrainHeight))
                {
                    Log.Warn("[DEBUG] Test explosion skipped: terrain height unavailable");
                    return;
                }

                pos.y = math.max(0f, terrainHeight);

                vfx.RequestExplosion(pos, ExplosionType.DirectHit);
                Log.Info($"[DEBUG] Test explosion at ({pos.x:F0},{pos.y:F0},{pos.z:F0})");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugTestExplosion failed: {ex}"); }
        }

        // ============================================================================
        // PRESETS
        // ============================================================================

        private void OnDebugRunPreset(int presetId)
        {
            try
            {
                if (World == null || !World.IsCreated) return;
                Log.Info($"[DEBUG] Running preset #{presetId}");
                var before = CaptureScenarioSnapshot();
                ScenarioLog.PresetSnapshot(presetId, ScenarioLogPhase.Before, before);

                switch (presetId)
                {
                    case 0:  SetAct(Act.Crisis); break;
                    case 1:  SetAct(Act.Crisis); SetShock(70f); SetExodusActive(true); break;
                    case 2:  SetShock(90f); SetExodusActive(true); SetIntegrity(0.1f); break;
                    case 3:  ForceGridCollapse(); break;
                    case 4:  SetStress(1.8f); break;
                    case 5:  SetIntegrity(0.3f); SetCorruption(40f); break;
                    case 6:  SetShock(80f); SetIntegrity(0.2f); SetExodusActive(true); break;
                    case 7:  ForceWave(20); SetShock(30f); break;
                    case 8:  SetCorruption(80f); SetHeat(70f); SetTrust(20f); break;
                    case 9:  ForceWave(15); SetStress(1.5f); break;
                    case 10: SetIntegrity(0.9f); SetShock(10f); SetTrust(80f); SetCorruption(5f); break;
                    case 99: ResetAllState(); break;
                    default:
                        Log.Warn($"[DEBUG] Unknown preset #{presetId}");
                        return;
                }

                var after = CaptureScenarioSnapshot();
                ScenarioLog.PresetSnapshot(presetId, ScenarioLogPhase.After, after);
                Log.Info($"[DEBUG] Preset #{presetId} applied");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugRunPreset failed: {ex}"); }
        }

        // ============================================================================
        // PRESET HELPERS
        // ============================================================================

        private void SetAct(Act act)
        {
            var ssm = World.GetExistingSystemManaged<ScenarioStateMachine>();
            if (ssm == null)
            {
                Log.Warn($"[DEBUG] SetAct({act}) skipped: ScenarioStateMachine unavailable");
                return;
            }
            if (act != Act.PreWar)
                GameTimeSystem.Instance?.DebugEnsureWarStarted();
            ssm.TransitionToAct(act);
        }

        private void SetShock(float level)
        {
            var mutator = ResolveShockMutator();
            if (mutator == null)
            {
                Log.Warn("[DEBUG] SetShock skipped: shock mutator unavailable");
                return;
            }
            mutator.DebugSetShockLevel(level, "scenario_preset");
        }

        private void SetExodusActive(bool active)
        {
            var mutator = ResolveExodusMutator();
            if (mutator == null)
            {
                Log.Warn("[DEBUG] SetExodusActive skipped: exodus mutator unavailable");
                return;
            }
            mutator.DebugSetExodusActive(active, "scenario_preset");
        }

        private void SetIntegrity(float value)
        {
            if (!FeatureRegistry.IsInitialized)
            {
                Log.Warn("[DEBUG] SetIntegrity skipped: FeatureRegistry unavailable");
                return;
            }
            var cognitive = FeatureRegistry.Instance.Query<CognitiveStateSystem>();
            if (cognitive == null || !cognitive.DebugOverrideIntegrity(value))
                Log.Warn("[DEBUG] SetIntegrity skipped: CognitiveStateSystem unavailable");
        }

        private void ForceGridCollapse()
        {
            ResolveGridStressSystem()?.DebugForceCollapse("scenario_preset");
        }

        private void SetStress(float hours)
        {
            ResolveGridStressSystem()?.DebugSetStressHours(hours, "scenario_preset");
        }

        private void ForceWave(int count)
        {
            var eventBus = GetEventBus();
            if (eventBus == null)
            {
                Log.Warn("[DEBUG] ForceWave skipped: EventBus unavailable");
                return;
            }
            GameTimeSystem.Instance?.DebugEnsureWarStarted();
            int waveNum = 99;
            if (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var ws))
                waveNum = ws.WaveNumber + 1;
            eventBus.SafePublish(
                new SpawnWaveRequestEvent(count, WaveNumber: waveNum, WaveType.MassiveStrike),
                "ScenarioDebugActionSystem.Preset"
            );
        }

        private IEventBus? GetEventBus()
        {
            if (m_EventBus == null)
                m_EventBus = ServiceRegistry.TryGet<IEventBus>();
            return m_EventBus;
        }

        private void SetCorruption(float value)
        {
            var mutator = ResolveCountermeasuresMutator();
            if (mutator == null)
            {
                Log.Warn("[DEBUG] SetCorruption skipped: countermeasures mutator unavailable");
                return;
            }
            mutator.DebugSetCorruption(value, "scenario_preset");
        }

        private void SetHeat(float value)
        {
            var mutator = ResolveCountermeasuresMutator();
            if (mutator == null)
            {
                Log.Warn("[DEBUG] SetHeat skipped: countermeasures mutator unavailable");
                return;
            }
            mutator.DebugSetHeat(value, "scenario_preset");
        }

        private void SetTrust(float value)
        {
            var mutator = ResolveReputationMutator();
            if (mutator == null)
            {
                Log.Warn("[DEBUG] SetTrust skipped: reputation mutator unavailable");
                return;
            }
            mutator.DebugSetTrust(value, "scenario_preset");
        }

        private void ResetAllState()
        {
            ResolveGridStressSystem()?.DebugResetStress("scenario_preset");
            ResolveShockMutator()?.DebugResetShock("scenario_preset");
            ResolveExodusMutator()?.DebugResetExodus("scenario_preset");
            ResolveCountermeasuresMutator()?.DebugResetCountermeasures("scenario_preset");
            if (FeatureRegistry.IsInitialized)
                FeatureRegistry.Instance.Query<CognitiveStateSystem>()?.ResetState();
            ResolveEconomyMutator()?.DebugResetEconomy("scenario_preset");
            ResolveReputationMutator()?.DebugResetReputation("scenario_preset");
            ResolveEnemyMutator()?.DebugResetEnemy("scenario_preset");
            ResolveMobilizationMutator()?.DebugResetMobilization("scenario_preset");

            Log.Info("[DEBUG] All state reset to defaults");
        }

        private GridStressSystem? ResolveGridStressSystem()
        {
            if (World == null || !World.IsCreated)
                return null;
            return World.GetExistingSystemManaged<GridStressSystem>();
        }

        private IShockDebugMutator ResolveShockMutator()
        {
            if (m_ShockMutator is null)
                m_ShockMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShockDebugMutator.Instance);
            return m_ShockMutator;
        }

        private IExodusDebugMutator ResolveExodusMutator()
        {
            if (m_ExodusMutator is null)
                m_ExodusMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullExodusDebugMutator.Instance);
            return m_ExodusMutator;
        }

        private ICountermeasuresDebugMutator ResolveCountermeasuresMutator()
        {
            if (m_CountermeasuresMutator is null)
                m_CountermeasuresMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCountermeasuresDebugMutator.Instance);
            return m_CountermeasuresMutator;
        }

        private IReputationDebugMutator ResolveReputationMutator()
        {
            if (m_ReputationMutator is null)
                m_ReputationMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullReputationDebugMutator.Instance);
            return m_ReputationMutator;
        }

        private IEnemyDebugMutator ResolveEnemyMutator()
        {
            if (m_EnemyMutator is null)
                m_EnemyMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullEnemyDebugMutator.Instance);
            return m_EnemyMutator;
        }

        private IMobilizationDebugMutator ResolveMobilizationMutator()
        {
            if (m_MobilizationMutator is null)
                m_MobilizationMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullMobilizationDebugMutator.Instance);
            return m_MobilizationMutator;
        }

        private IEconomyDebugMutator ResolveEconomyMutator()
        {
            if (m_EconomyMutator is null)
                m_EconomyMutator = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullEconomyDebugMutator.Instance);
            return m_EconomyMutator;
        }

        private ScenarioLogSnapshot CaptureScenarioSnapshot()
        {
            Act act = Act.PreWar;
            if (SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                act = actSingleton.CurrentAct;

            float shockLevel = 0f;
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock))
                shockLevel = shock.ShockLevel;

            float stressHours = 0f;
            if (m_GridStressQuery.TryGetSingleton<GridStressData>(out var grid))
                stressHours = Math.Max(0f, grid.StressHours);

            bool exodusActive = false;
            if (m_ExodusQuery.TryGetSingleton<ExodusStateSingleton>(out var exodus))
                exodusActive = exodus.IsExodusActive;

            return new ScenarioLogSnapshot(
                act,
                shockLevel,
                stressHours,
                GetCityIntegrity(),
                exodusActive,
                this.GetCitizenCount());
        }

        private float GetCityIntegrity()
        {
            if (!m_CognitiveQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return 1f;

            m_CogIntegrityBufferLookup.Update(this);
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
