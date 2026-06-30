using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.UI;
using CivicSurvival.Domains.PowerBackup.Systems;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.PowerBackup.UI
{
    /// <summary>
    /// UI system for backup power stats and District Modernization.
    /// Uses ServiceRegistry for cross-domain interface access.
    ///
    /// Migrated from BackupPowerUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class BackupPowerUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private IDistrictModernizationService m_ShadowProcurement = null!;
        private BackupPowerRuntimeSystem? m_RuntimeSystem;
        private BackupPowerEffectsSystem? m_EffectsSystem;

        private string m_CachedProgramsJson = JsonBuilder.EmptyArray;
        private readonly List<int> m_KnownDistrictsSnapshot = new();
        private const int MAX_DISTRICT_INDEX = 10000;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_RuntimeSystem = World.GetExistingSystemManaged<BackupPowerRuntimeSystem>();
            // Read effects stats directly from system (no ECS singleton — eliminates sync point)
            m_EffectsSystem = World.GetExistingSystemManaged<BackupPowerEffectsSystem>();

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(BackupPowerState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(
                LaunchDistrictModernization,
                FeatureIds.PowerBackup,
                RequestResultBridge.Modernization,
                ActionKey.BackupModernization,
                BuildActionContext,
                OnLaunchDistrictModernization);
            Triggers.AddScenarioTrigger<int>(SetBackupPolicy, FeatureIds.PowerBackup, Act.Crisis, RequestResultBridge.BackupPolicy, OnSetBackupPolicy);
        }

        protected override void OnPanelUpdate()
        {
            m_ShadowProcurement ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDistrictModernizationService.Instance);
            m_RuntimeSystem ??= World.GetExistingSystemManaged<BackupPowerRuntimeSystem>();
            m_EffectsSystem ??= World.GetExistingSystemManaged<BackupPowerEffectsSystem>();

            var dto = new BackupPowerDto();
            dto.CanSetBackupPolicy = true;
            dto.SetBackupPolicyLockedReasonId = "";
            if (SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                && !actSingleton.TryRequireAct(Act.Crisis, out var backupPolicyReason))
            {
                dto.CanSetBackupPolicy = false;
                dto.SetBackupPolicyLockedReasonId = backupPolicyReason;
            }

            if (m_RuntimeSystem != null)
            {
                var backupPower = m_RuntimeSystem.UiState;
                dto.BackupCharge = (int)backupPower.ChargePercent;
                dto.ProtectedBuildings = backupPower.ProtectedBuildings;
                dto.BackupCapacity = backupPower.TotalCapacityKWh;
                dto.DischargingCount = backupPower.DischargingCount;
                dto.BackupPolicy = (int)backupPower.Policy;
                dto.HospitalsPowered = backupPower.HospitalsPowered;
                dto.HospitalsTotal = backupPower.HospitalsTotal;
                dto.SchoolsPowered = backupPower.SchoolsPowered;
                dto.SchoolsTotal = backupPower.SchoolsTotal;
            }

            if (SystemAPI.TryGetSingleton<BackupPowerStateSingleton>(out var backupPowerState))
            {
                dto.BackupPolicy = (int)backupPowerState.Policy;
            }

            if (m_EffectsSystem != null && m_EffectsSystem.Enabled)
            {
                var effects = m_EffectsSystem.Stats;
                dto.GeneratorsRunning = effects.GeneratorsRunning;
                dto.NoiseLevel = effects.TotalNoiseLevel;
            }

            dto.ProcurementCooldown = m_ShadowProcurement.DaysUntilNextProcurement;
            m_CachedProgramsJson = BuildProgramsJson();
            dto.ShadowProgramsJson = m_CachedProgramsJson;
            dto.ModernizationRequestJson = RequestResultBridge.Get(RequestResultBridge.Modernization).ToJson();
            dto.BackupPolicyRequestJson = RequestResultBridge.Get(RequestResultBridge.BackupPolicy).ToJson();

            PublishWhenComplete(BackupPowerState, NoSourceChecks, () => dto);
        }

        private string BuildProgramsJson()
        {
            var knownDistricts = m_ShadowProcurement.KnownDistricts;
            int count = knownDistricts.Count;

            if (count == 0)
            {
                return JsonBuilder.EmptyArray;
            }

            m_KnownDistrictsSnapshot.Clear();
            if (m_KnownDistrictsSnapshot.Capacity < count)
                m_KnownDistrictsSnapshot.Capacity = count;
            foreach (var districtIndex in knownDistricts)
                m_KnownDistrictsSnapshot.Add(districtIndex);
            m_KnownDistrictsSnapshot.Sort();

            var sb = new StringBuilder(1024);
            sb.Append('[');
            bool first = true;
            var modernizationGate = ActionGate.Resolve(ActionKey.BackupModernization, BuildActionContext());
            foreach (var districtIndex in m_KnownDistrictsSnapshot)
            {
                var program = m_ShadowProcurement.GetProgram(districtIndex) ?? default;
                // FIX S4-06: Modernization uses CityBudget (not ShadowWallet) — no sanctions markup
                int estimatedCost = m_ShadowProcurement.GetEstimatedCost(districtIndex);
                bool canModernizeHonest = false;
                bool canModernizeCorrupt = false;
                string honestReason = modernizationGate.LockedReasonId;
                string corruptReason = modernizationGate.LockedReasonId;
                if (modernizationGate.CanRun)
                {
                    var honestEligibility = m_ShadowProcurement.GetModernizationEligibility(districtIndex, ContractorType.Honest);
                    var corruptEligibility = m_ShadowProcurement.GetModernizationEligibility(districtIndex, ContractorType.YourGuy);
                    canModernizeHonest = honestEligibility.CanRun;
                    honestReason = honestEligibility.LockedReasonId;
                    canModernizeCorrupt = corruptEligibility.CanRun;
                    corruptReason = corruptEligibility.LockedReasonId;
                }
                string contractorName;
                if (program.Contractor == ContractorType.Honest) contractorName = "Honest";
                else if (program.Contractor == ContractorType.YourGuy) contractorName = "YourGuy";
                else contractorName = "None";

                var entry = new ShadowProgramEntry
                {
                    DistrictIndex = districtIndex,
                    DistrictName = $"District {districtIndex}",
                    HasProgram = program.HasProgram,
                    Contractor = contractorName,
                    EstimatedCost = estimatedCost,
                    CanModernizeHonest = canModernizeHonest,
                    ModernizeHonestLockedReasonId = honestReason,
                    CanModernizeCorrupt = canModernizeCorrupt,
                    ModernizeCorruptLockedReasonId = corruptReason,
                    KickbackEarned = program.KickbackEarned,
                    FireCount = program.FireCount,
                };
                if (!first) sb.Append(',');
                first = false;
                entry.WriteTo(sb);
            }

            sb.Append(']');
            return sb.ToString();
        }

        private ActionContext BuildActionContext()
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            return new ActionContext(
                false,
                GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);
        }

        private TriggerOutcome OnLaunchDistrictModernization(int encodedValue)
        {
            if (encodedValue < 0) return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            int districtIndex = encodedValue / 10;
            int contractorInt = encodedValue % 10;
            if (districtIndex < 0 || districtIndex > MAX_DISTRICT_INDEX)
                return TriggerOutcome.Reject(ReasonIds.DistrictInvalidInput);
            if (contractorInt < 0 || contractorInt > 1)
                return TriggerOutcome.Reject(ReasonIds.BackupModernizationInvalidContractor);
            m_ShadowProcurement ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullDistrictModernizationService.Instance);
            if (FeatureRegistry.IsInitialized && !FeatureRegistry.Instance.IsAvailable(FeatureIds.Corruption))
                return TriggerOutcome.Reject(ReasonIds.BackupModernizationConfigError);
            ContractorType contractor = contractorInt == 1 ? ContractorType.YourGuy : ContractorType.Honest;
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("District modernization rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ModernizationRequest
            {
                DistrictIndex = districtIndex,
                Contractor = contractor
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created ModernizationRequest: district {districtIndex}, contractor {contractor}");
            return TriggerOutcome.HandOffToEcs(
                ecb,
                entity,
                SystemAPI.Time.ElapsedTime,
                TriggerOutcome.CurrentSimulationFrame(World),
                "districtIndex",
                districtIndex.ToString());
        }

        private TriggerOutcome OnSetBackupPolicy(in ScenarioGuard guard, int policyInt)
        {
            if (policyInt < 0 || policyInt > 2)
            {
                return TriggerOutcome.Reject(ReasonIds.BackupPolicyInvalid);
            }

            var policy = (CivicSurvival.Core.Types.BackupPolicy)policyInt;
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new BackupPolicyRequest
            {
                Policy = policy
            });
            Log.Info($"Created BackupPolicyRequest: {policy}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }
    }
}
