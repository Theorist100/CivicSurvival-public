using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using B = CivicSurvival.Core.UI.B;
using HeroStatusEnum = CivicSurvival.Core.Components.CrossDomain.HeroStatus;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems;
using CivicSurvival.Domains.Cognitive.Ops.Systems;
using CivicSurvival.Localization;

namespace CivicSurvival.Domains.Cognitive.UI
{
    /// <summary>
    /// UI system for Cognitive domain data.
    /// ECS-Pure: Reads directly from CognitiveState singleton.
    ///
    /// Migrated from CognitiveUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// Removes #pragma warning disable CIVIC051 (now proper ECS system).
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.InternetMode)]
    [HandlesRequestKind(RequestKind.TelemarathonMode)]
    [HandlesRequestKind(RequestKind.TelemarathonActive)]
    public partial class CognitiveUISystem : CivicUIPanelSystem
    {
        // ===== Narrative Integrity Thresholds =====
        private const float VIGILANT_THRESHOLD = 0.8f;
        private const float QUESTIONING_THRESHOLD = 0.7f;
        private const float ANGER_THRESHOLD = 0.3f;

        private EndFrameBarrier m_EndFrameBarrier = null!;
        private HeroDeploymentSystem? m_HeroDeploymentSystem;
        private CognitiveStateSystem? m_CognitiveStateSystem;
        private TelemarathonSystem? m_TelemarathonSystem;
        private EntityQuery m_StateQuery;
        private EntityQuery m_SpotterCmQuery;
        private EntityQuery m_StatsQuery;
        private EntityQuery m_DistrictPowerQuery;
        private EntityQuery m_IpsoQuery;
        private EntityQuery m_TelemarathonQuery;

        private ComponentLookup<CognitiveState> m_StateLookup;
        private ComponentLookup<HeroDeploymentState> m_HeroStateLookup;
        private ComponentLookup<SpotterCountermeasuresState> m_SpotterCmLookup;
        private ComponentLookup<CognitiveStatsState> m_StatsLookup;
        private ComponentLookup<TelemarathonRuntimeState> m_TelemarathonLookup;
        private ComponentLookup<IPSOState> m_IpsoLookup;
        private BufferLookup<InternetDisabledBuffer> m_InternetDisabledBufferLookup;
        private BufferLookup<CognitiveIntegrityBuffer> m_IntegrityBufferLookup;
        private BufferLookup<DistrictEntityEntry> m_DistrictEntityBufferLookup;

        // District data list, serialized to a JSON array string binding
        private readonly List<CognitiveDistrictEntry> m_DistrictsList = new();

        // HashSet for internet disabled lookup (reused to avoid GC)
        // Keys are district indices
        [NonEntityIndex] private readonly HashSet<int> m_InternetDisabledLookup = new();

        // District name resolution
        private NameSystemFacade? m_NameService;
        private readonly Dictionary<long, string> m_NameCache = new();
        private readonly Dictionary<long, Entity> m_EntityLookup = new();
        [NonEntityIndex] private readonly Dictionary<int, long> m_DistrictKeyByIndex = new();
        private int m_NameCacheRefreshCounter;
        private const int NAME_CACHE_REFRESH_INTERVAL = 300;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_StateQuery = GetEntityQuery(
                ComponentType.ReadOnly<CognitiveState>());
            m_SpotterCmQuery = GetEntityQuery(
                ComponentType.ReadOnly<SpotterCountermeasuresState>());
            m_StatsQuery = GetEntityQuery(
                ComponentType.ReadOnly<CognitiveStatsState>());
            m_TelemarathonQuery = GetEntityQuery(
                ComponentType.ReadOnly<TelemarathonRuntimeState>());
            m_DistrictPowerQuery = GetEntityQuery(
                ComponentType.ReadOnly<DistrictPowerBufferSingleton>());
            m_IpsoQuery = GetEntityQuery(
                ComponentType.ReadOnly<IPSOState>());
            m_StateLookup = GetComponentLookup<CognitiveState>(true);
            m_HeroStateLookup = GetComponentLookup<HeroDeploymentState>(true);
            m_SpotterCmLookup = GetComponentLookup<SpotterCountermeasuresState>(true);
            m_StatsLookup = GetComponentLookup<CognitiveStatsState>(true);
            m_TelemarathonLookup = GetComponentLookup<TelemarathonRuntimeState>(true);
            m_IpsoLookup = GetComponentLookup<IPSOState>(true);
            m_InternetDisabledBufferLookup = GetBufferLookup<InternetDisabledBuffer>(true);
            m_IntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);
            m_DistrictEntityBufferLookup = GetBufferLookup<DistrictEntityEntry>(true);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_HeroDeploymentSystem ??= World.GetExistingSystemManaged<HeroDeploymentSystem>();
            m_CognitiveStateSystem ??= World.GetExistingSystemManaged<CognitiveStateSystem>();
            m_TelemarathonSystem ??= World.GetExistingSystemManaged<TelemarathonSystem>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(B.CognitiveState, "{}");
            // Unified string-binding pipeline: JSON array string parsed on the UI
            // by useSafeJsonArray. BindingRegistry dedups identical payloads.
            Bindings.Add<string>(B.CognitiveDistricts, JsonBuilder.EmptyArray);
        }

        protected override void ConfigureTriggers()
        {
            Triggers.AddWarTrigger<int>(B.DeployHero, FeatureIds.Cognitive, RequestResultBridge.HeroAction, OnDeployHero);
            Triggers.Add(B.RecallHero, FeatureIds.Cognitive, RequestResultBridge.HeroAction, OnRecallHero);
            Triggers.AddWarTrigger<int>(B.SetHeroMode, FeatureIds.Cognitive, RequestResultBridge.HeroAction, OnSetHeroMode);

            // Telemarathon triggers
            Triggers.Add<int>(B.SetNarrativeMode, FeatureIds.Cognitive, RequestResultBridge.TelemarathonMode, OnSetNarrativeMode);
            Triggers.Add<bool>(B.SetTelemarathonActive, FeatureIds.Cognitive, RequestResultBridge.TelemarathonActive, OnSetTelemarathonActive);

            // Global Internet Mode trigger
            Triggers.AddWarTrigger<int>(B.SetInternetMode, FeatureIds.Cognitive, RequestResultBridge.InternetMode, OnSetInternetMode);
        }

        protected override void OnPanelUpdate()
        {
            // Refresh name cache periodically (picks up renamed districts)
            if (++m_NameCacheRefreshCounter >= NAME_CACHE_REFRESH_INTERVAL)
            {
                m_NameCacheRefreshCounter = 0;
                m_NameCache.Clear();
            }

            // Read state from singleton
            if (!m_StateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return;

            UpdateLookups();
            if (!m_StateLookup.HasComponent(stateEntity))
                return;

            var state = m_StateLookup[stateEntity];
            var heroState = SystemAPI.TryGetSingleton<HeroDeploymentState>(out var hs)
                ? hs : HeroDeploymentState.Default;

            var dto = new CognitiveDto
            {
                CognitiveActive = state.IsActive,
                InfectionRate = CognitiveRates.EffectiveInfectionRate(state, heroState),
                RecoveryRate = CognitiveRates.EffectiveRecoveryRate(state, heroState),
                PenaltyThreshold = state.CompromiseThreshold,
                InternetMode = (int)state.InternetMode,
                CommercePenalty = state.CurrentCommercePenalty,
                HeroStatus = (int)heroState.HeroStatus,
                HeroDeployCost = heroState.HeroDeployCost,
                HeroInfectionReduction = heroState.HeroInfectionReduction,
                HeroRecoveryBonus = heroState.HeroRecoveryBonus,
                HeroActionRequestJson = RequestResultBridge.Get(RequestResultBridge.HeroAction).ToJson(),
                InternetModeRequestJson = RequestResultBridge.Get(RequestResultBridge.InternetMode).ToJson(),
                TelemarathonModeRequestJson = RequestResultBridge.Get(RequestResultBridge.TelemarathonMode).ToJson(),
                TelemarathonActiveRequestJson = RequestResultBridge.Get(RequestResultBridge.TelemarathonActive).ToJson()
            };
            FillHeroEligibility(ref dto);

            // Build internet disabled lookup
            m_InternetDisabledLookup.Clear();
            if (m_SpotterCmQuery.TryGetSingletonEntity<SpotterCountermeasuresState>(out var cmEntity)
                && m_SpotterCmLookup.HasComponent(cmEntity)
                && m_InternetDisabledBufferLookup.HasBuffer(cmEntity))
            {
                var internetBuffer = m_InternetDisabledBufferLookup[cmEntity];
                for (int i = 0; i < internetBuffer.Length; i++)
                {
                    m_InternetDisabledLookup.Add(internetBuffer[i].DistrictIndex);
                }
            }

            // Read integrity buffer
            if (!m_IntegrityBufferLookup.HasBuffer(stateEntity)) return;
            var buffer = m_IntegrityBufferLookup[stateEntity];

            // Rebuild entity lookup for name resolution (~20 entries, trivial at 500ms throttle)
            RebuildEntityLookup();

            int compromisedCount = 0;
            float totalIntegrity = 0f;
            m_DistrictsList.Clear();

            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                totalIntegrity += entry.Integrity;
                if (entry.IsCompromised)
                    compromisedCount++;

                bool hasInternet = CognitiveDistrictEligibility.CanInternetReach(
                    state.InternetMode,
                    entry.DistrictIndex,
                    m_InternetDisabledLookup.Contains(entry.DistrictIndex));

                string districtName;
                if (entry.DistrictIndex == 0)
                {
                    districtName = LocalizationManager.T("UI_DISTRICT_UNZONED");
                }
                else if (m_DistrictKeyByIndex.TryGetValue(entry.DistrictIndex, out var districtKey)
                    && m_NameCache.TryGetValue(districtKey, out var cachedName)
                    && IsCachedDistrictNameStillValid(districtKey))
                {
                    districtName = cachedName;
                }
                else
                {
                    if (m_DistrictKeyByIndex.TryGetValue(entry.DistrictIndex, out districtKey))
                        m_NameCache.Remove(districtKey);
                    var resolved = ResolveDistrictName(entry.DistrictIndex);
                    if (resolved != null)
                    {
                        if (m_DistrictKeyByIndex.TryGetValue(entry.DistrictIndex, out districtKey))
                            m_NameCache[districtKey] = resolved;
                        districtName = resolved;
                    }
                    else
                    {
                        districtName = $"District {entry.DistrictIndex}";
                    }
                }

                m_DistrictsList.Add(new CognitiveDistrictEntry
                {
                    DistrictIndex = entry.DistrictIndex,
                    Name = districtName,
                    Integrity = entry.Integrity,
                    HasInternet = hasInternet,
                    IsCompromised = entry.IsCompromised,
                    IsUnzoned = entry.DistrictIndex == 0
                });
            }

            m_DistrictsList.Sort((a, b) => a.Integrity.CompareTo(b.Integrity));

            float avgIntegrity = buffer.Length > 0 ? totalIntegrity / buffer.Length : 1f;

            dto.TotalDistricts = buffer.Length;
            dto.CompromisedDistricts = compromisedCount;
            dto.AvgIntegrity = avgIntegrity;
            dto.ProtestRisk = CalculateProtestRisk(avgIntegrity);
            dto.DominantNarrative = GetDominantNarrative(avgIntegrity, state.IsActive);
            PublishJsonWhenComplete(B.CognitiveDistricts, NoSourceChecks, () => SerializeCognitiveDistricts(m_DistrictsList));

            // Household-level stats
            if (m_StatsQuery.TryGetSingletonEntity<CognitiveStatsState>(out var statsEntity))
            {
                var stats = m_StatsLookup[statsEntity];
                dto.TotalHouseholds = stats.TotalHouseholds;
                dto.AvgInfection = stats.AvgInfectionLevel;
                dto.AvgResistance = stats.AvgResistance;
                dto.AvgTrauma = stats.AvgTrauma;
                dto.HouseholdsUnderBlackout = stats.HouseholdsUnderBlackout;
                dto.HouseholdsWithEnvy = stats.HouseholdsWithEnvy;
                dto.HouseholdsUnderImpact = stats.HouseholdsUnderImpact;
                dto.HouseholdsInfected = stats.HouseholdsInfected;
                dto.VulnerableHouseholds = stats.VulnerableHouseholds;
                dto.AvgBlackoutHours = stats.AvgBlackoutHours;
                dto.BlackoutVulnerability = stats.BlackoutVulnerabilityMult;
            }

            // Telemarathon state
            if (m_TelemarathonQuery.TryGetSingletonEntity<TelemarathonRuntimeState>(out var telemarathonEntity))
            {
                var telemarathon = m_TelemarathonLookup[telemarathonEntity];
                dto.TelemarathonActive = telemarathon.IsActive;
                dto.NarrativeMode = (int)telemarathon.Mode;
                dto.MediaTrust = telemarathon.Trust;
                dto.IsInShock = telemarathon.IsInShock;
                dto.ShockHoursRemaining = telemarathon.ShockHoursRemaining;
                dto.AudienceFatigue = telemarathon.AudienceFatigue;
            }

            // IPSO state
            if (m_IpsoQuery.TryGetSingletonEntity<IPSOState>(out var ipsoEntity))
            {
                var ipso = m_IpsoLookup[ipsoEntity];
                dto.IpsoActive = ipso.IsActive;
                dto.IpsoIntensity = (int)Math.Round(ipso.GlobalExposure * 100f);
                dto.IpsoDistrictCount = ipso.AffectedDistrictCount;
                dto.IpsoTotalDistricts = ipso.TotalDistrictCount;
            }

            PublishWhenComplete(B.CognitiveState, NoSourceChecks, () => dto);
        }

        private void UpdateLookups()
        {
            m_StateLookup.Update(this);
            m_HeroStateLookup.Update(this);
            m_SpotterCmLookup.Update(this);
            m_StatsLookup.Update(this);
            m_TelemarathonLookup.Update(this);
            m_IpsoLookup.Update(this);
            m_InternetDisabledBufferLookup.Update(this);
            m_IntegrityBufferLookup.Update(this);
            m_DistrictEntityBufferLookup.Update(this);
        }

        /// <summary>
        /// Build entity index → Entity map from DistrictEntityEntry buffer (one pass).
        /// Called on cache refresh so ResolveDistrictName does O(1) lookup instead of O(N) scan.
        /// </summary>
        private void RebuildEntityLookup()
        {
            m_EntityLookup.Clear();
            m_DistrictKeyByIndex.Clear();
            if (m_DistrictPowerQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var powerSingleton)
                && m_DistrictEntityBufferLookup.HasBuffer(powerSingleton))
            {
                var entityBuffer = m_DistrictEntityBufferLookup[powerSingleton];
                for (int i = 0; i < entityBuffer.Length; i++)
                {
                    var districtRef = entityBuffer[i].District;
                    var entity = districtRef.ToEntity();
                    if (World.EntityManager.Exists(entity))
                    {
                        long districtKey = districtRef.Packed;
                        m_EntityLookup[districtKey] = entity;
                        m_DistrictKeyByIndex[districtRef.Index] = districtKey;
                    }
                }
            }
        }

        /// <summary>
        /// Lazy name resolution via cached entity lookup (O(1) per district).
        /// </summary>
        private string? ResolveDistrictName(int districtIndex)
        {
            if (m_NameService == null)
                m_NameService = ServiceRegistry.Instance.Require<NameSystemFacade>();
            if (m_NameService == null) return null;

            // FIX L10: Validate entity still exists (district may have been deleted/recycled)
            if (m_DistrictKeyByIndex.TryGetValue(districtIndex, out var districtKey)
                && m_EntityLookup.TryGetValue(districtKey, out var entity)
                && World.EntityManager.Exists(entity))
            {
                string name = m_NameService.GetRenderedLabelName(entity);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return null;
        }

        private bool IsCachedDistrictNameStillValid(long districtKey)
        {
            return m_EntityLookup.TryGetValue(districtKey, out var entity)
                && World.EntityManager.Exists(entity);
        }

        private static int CalculateProtestRisk(float avgIntegrity)
        {
            if (avgIntegrity > QUESTIONING_THRESHOLD) return 0;
            if (avgIntegrity > 0.5f) return 1;
            if (avgIntegrity > ANGER_THRESHOLD) return 2;
            return 3;
        }

        private static string GetDominantNarrative(float avgIntegrity, bool isActive)
        {
            if (!isActive)
                return "All quiet on the information front.";

            if (avgIntegrity > VIGILANT_THRESHOLD)
                return "People are vigilant but calm.";
            if (avgIntegrity > QUESTIONING_THRESHOLD)
                return "Some are questioning official statements...";
            if (avgIntegrity > 0.5f)
                return "\"Why is the government hiding the truth?!\"";
            if (avgIntegrity > ANGER_THRESHOLD)
                return "\"How dare you turn off the Wi-Fi?!\"";
            if (avgIntegrity > 0.1f)
                return "\"The mayor is a puppet! Wake up, sheeple!\"";

            return "\"BURN IT ALL DOWN! STORM THE CITY HALL!\"";
        }

        private void FillHeroEligibility(ref CognitiveDto dto)
        {
            m_HeroDeploymentSystem ??= World.GetExistingSystemManaged<HeroDeploymentSystem>();
            if (m_HeroDeploymentSystem == null)
            {
                dto.CanDeployHero = false;
                dto.DeployHeroLockedReasonId = ReasonIds.HeroSystemUnavailable;
                dto.CanRecallHero = false;
                dto.RecallHeroLockedReasonId = ReasonIds.HeroSystemUnavailable;
                dto.CanSetHeroCounter = false;
                dto.SetHeroCounterLockedReasonId = ReasonIds.HeroSystemUnavailable;
                dto.CanSetHeroLecturing = false;
                dto.SetHeroLecturingLockedReasonId = ReasonIds.HeroSystemUnavailable;
                return;
            }

            dto.CanDeployHero = m_HeroDeploymentSystem.CanDeployHero(HeroStatusEnum.Deployed, out var deployReason);
            dto.DeployHeroLockedReasonId = deployReason;
            dto.CanRecallHero = m_HeroDeploymentSystem.CanRecallHero(out var recallReason);
            dto.RecallHeroLockedReasonId = recallReason;
            dto.CanSetHeroCounter = m_HeroDeploymentSystem.CanSetHeroMode(HeroStatusEnum.Deployed, out var counterReason);
            dto.SetHeroCounterLockedReasonId = counterReason;
            dto.CanSetHeroLecturing = m_HeroDeploymentSystem.CanSetHeroMode(HeroStatusEnum.Lecturing, out var lecturingReason);
            dto.SetHeroLecturingLockedReasonId = lecturingReason;
        }

        private TriggerOutcome OnDeployHero(in ScenarioGuard guard, int mode)
        {
            if (!TryMapHeroMode(mode, allowInactive: false, out var heroMode))
            {
                Log.Warn($"DeployHero rejected: invalid mode {mode}");
                return TriggerOutcome.Reject(ReasonIds.HeroInvalidMode);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("DeployHero rejected: budget pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new HeroActionRequest
            {
                Action = HeroActionType.Deploy,
                Mode = heroMode
            });
            Log.Info($"Created HeroActionRequest: Deploy {heroMode}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnRecallHero()
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("RecallHero rejected: hero action pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new HeroActionRequest
            {
                Action = HeroActionType.Recall,
                Mode = HeroStatusEnum.Inactive
            });
            Log.Info("Created HeroActionRequest: Recall");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnSetHeroMode(in ScenarioGuard guard, int mode)
        {
            if (!TryMapHeroMode(mode, allowInactive: true, out var heroMode))
            {
                Log.Warn($"SetHeroMode rejected: invalid mode {mode}");
                return TriggerOutcome.Reject(ReasonIds.HeroInvalidMode);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info($"SetHeroMode rejected: hero action pipeline requires unpaused simulation for {heroMode}");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new HeroActionRequest
            {
                Action = HeroActionType.SetMode,
                Mode = heroMode
            });
            Log.Info($"Created HeroActionRequest: SetMode {heroMode}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnSetNarrativeMode(int mode)
        {
            if (!TryMapNarrativeMode(mode, out var narrativeMode))
            {
                Log.Warn($"SetNarrativeMode rejected: invalid mode {mode}");
                return TriggerOutcome.Reject(ReasonIds.CognitiveInvalidNarrativeMode);
            }

            m_TelemarathonSystem ??= World.GetExistingSystemManaged<TelemarathonSystem>();
            if (m_TelemarathonSystem == null)
            {
                Log.Warn("SetNarrativeMode rejected: TelemarathonSystem unavailable");
                return TriggerOutcome.Reject(ReasonIds.GenericSingletonNotReady);
            }

            if (!m_TelemarathonSystem.TrySetMode(narrativeMode, out var reasonId))
                return TriggerOutcome.Reject(reasonId);

            Log.Info($"SetNarrativeMode applied synchronously: {narrativeMode}");
            return TriggerOutcome.SyncSuccess();
        }

        private TriggerOutcome OnSetTelemarathonActive(bool active)
        {
            m_TelemarathonSystem ??= World.GetExistingSystemManaged<TelemarathonSystem>();
            if (m_TelemarathonSystem == null)
            {
                Log.Warn("SetTelemarathonActive rejected: TelemarathonSystem unavailable");
                return TriggerOutcome.Reject(ReasonIds.GenericSingletonNotReady);
            }

            if (!m_TelemarathonSystem.TrySetActive(active, out var reasonId))
                return TriggerOutcome.Reject(reasonId);

            Log.Info($"SetTelemarathonActive applied synchronously: {active}");
            return TriggerOutcome.SyncSuccess();
        }

        private TriggerOutcome OnSetInternetMode(in ScenarioGuard guard, int mode)
        {
            if (!TryMapInternetMode(mode, out var requestMode))
            {
                Log.Warn($"SetInternetMode rejected: invalid mode {mode}");
                return TriggerOutcome.Reject(ReasonIds.CognitiveInvalidMode);
            }

            m_CognitiveStateSystem ??= World.GetExistingSystemManaged<CognitiveStateSystem>();
            if (m_CognitiveStateSystem == null)
            {
                Log.Warn("SetInternetMode rejected: CognitiveStateSystem unavailable");
                return TriggerOutcome.Reject(ReasonIds.GenericSingletonNotReady);
            }

            if (!m_CognitiveStateSystem.TrySetInternetMode(requestMode, out var reasonId))
                return TriggerOutcome.Reject(reasonId);

            Log.Info($"SetInternetMode applied synchronously: {requestMode}");
            return TriggerOutcome.SyncSuccess();
        }

        private static bool TryMapHeroMode(int mode, bool allowInactive, out HeroStatusEnum heroMode)
        {
            switch (mode)
            {
                case 0 when allowInactive:
                    heroMode = HeroStatusEnum.Inactive;
                    return true;
                case 1:
                    heroMode = HeroStatusEnum.Deployed;
                    return true;
                case 2:
                    heroMode = HeroStatusEnum.Lecturing;
                    return true;
                default:
                    heroMode = default;
                    return false;
            }
        }

        private static bool TryMapNarrativeMode(int mode, out NarrativeMode narrativeMode)
        {
            switch (mode)
            {
                case 0:
                    narrativeMode = NarrativeMode.Soothing;
                    return true;
                case 1:
                    narrativeMode = NarrativeMode.Alarmist;
                    return true;
                case 2:
                    narrativeMode = NarrativeMode.Realistic;
                    return true;
                default:
                    narrativeMode = default;
                    return false;
            }
        }

        private static bool TryMapInternetMode(int mode, out GlobalInternetMode internetMode)
        {
            switch (mode)
            {
                case 0:
                    internetMode = GlobalInternetMode.Open;
                    return true;
                case 1:
                    internetMode = GlobalInternetMode.Firewall;
                    return true;
                case 2:
                    internetMode = GlobalInternetMode.Blackout;
                    return true;
                default:
                    internetMode = default;
                    return false;
            }
        }

        private static string SerializeCognitiveDistricts(List<CognitiveDistrictEntry> districts)
        {
            if (districts == null || districts.Count == 0) return JsonBuilder.EmptyArray;
            var sb = new StringBuilder(512);
            sb.Append('[');
            for (int i = 0; i < districts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                districts[i].WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    // CognitiveDistrictEntry moved to Core/UI/DomainState/CognitiveDistrictEntry.cs
    // in C8; serialization goes through the generated WriteTo.
}
