using System;
using Unity.Collections;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Domains.Mobilization.Systems;
using CivicSurvival.Core.Logic;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.Mobilization.UI
{
    /// <summary>
    /// UI system for Mobilization domain.
    /// Displays manpower availability, modifiers, and conscription controls.
    ///
    /// Migrated from MobilizationUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQuery, RequireForUpdate, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [TransientConsumerReconcile(typeof(MobilizationRequest), ReconcileMode.NoDurableSideEffect)]
    public partial class MobilizationUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_MobilizationQuery;
        private EntityQuery m_MobilizationRequestQuery;
        private string m_LastMobilizationJson = string.Empty;
        private bool m_HasPendingConscriptionTarget;
        private bool m_PendingConscriptionTarget;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            m_MobilizationQuery = GetEntityQuery(
                ComponentType.ReadOnly<MobilizationStateSingleton>());
            m_MobilizationRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<MobilizationRequest>());

            RequireForUpdate(m_MobilizationQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            RefreshPendingConscriptionTargetFromLiveRequests();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(MobilizationState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.AddScenarioTrigger(ToggleConscription, FeatureIds.Mobilization, Act.Crisis, RequestResultBridge.ConscriptionToggle, OnToggleConscription);
            Triggers.AddScenarioTrigger(CallToArms, FeatureIds.Mobilization, Act.Crisis, RequestResultBridge.CallToArms, OnCallToArms);
        }

        protected override void OnPanelUpdate()
        {
            // NO_MIGRATE: panel render skips when Mobilization state is absent.
            if (!m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var state)) return;

            if (!m_HasPendingConscriptionTarget)
                RefreshPendingConscriptionTargetFromLiveRequests();

            if (m_HasPendingConscriptionTarget && state.IsConscriptionActive == m_PendingConscriptionTarget)
            {
                m_HasPendingConscriptionTarget = false;
            }

            int percent = state.TotalManpower > 0
                ? (int)Math.Round(100f * state.AvailableManpower / state.TotalManpower)
                : 0;

            bool callToArmsOnCooldown = IsCallToArmsOnCooldown(state);
            bool conscriptionOnCooldown = IsConscriptionReactivationOnCooldown(state);

            var dto = new MobilizationDto
            {
                ManpowerAvailable = state.AvailableManpower,
                ManpowerUsed = state.UsedManpower,
                ManpowerTotal = state.TotalManpower,
                ManpowerPercent = percent,
                ManpowerBasePool = state.BasePool,
                ManpowerCasualties = state.Casualties,
                ManpowerPatriotismFactor = (int)Math.Round(state.PatriotismFactor * 100),
                ManpowerMoraleFactor = (int)Math.Round(state.MoraleFactor * 100),
                ManpowerFatigueFactor = (int)Math.Round(state.FatigueFactor * 100),
                IsConscriptionActive = state.IsConscriptionActive,
                IsWarFatigued = state.IsWarFatigued,
                IsManpowerCritical = ManpowerLogic.IsCritical(state.AvailableManpower, state.TotalManpower),
                IsManpowerOvercommitted = state.UsedManpower > state.TotalManpower,
                CallToArmsOnCooldown = callToArmsOnCooldown,
                ConscriptionReactivationOnCooldown = conscriptionOnCooldown,
                PredictedConscriptionRelease = state.PredictedConscriptionRelease,
                SocialPenaltyProducerReady = state.SocialPenaltyProducerReady,
                SocialPenaltyReasonId = state.SocialPenaltyProducerReady ? "" : ReasonIds.MobSocialPenaltyUnavailable,
                WarDay = state.WarDay,
                CallToArmsRequestJson = RequestResultBridge.Get(RequestResultBridge.CallToArms).ToJson(),
                ConscriptionToggleRequestJson = RequestResultBridge.Get(RequestResultBridge.ConscriptionToggle).ToJson()
            };
            FillEligibility(ref dto, state, callToArmsOnCooldown, conscriptionOnCooldown);

            var sb = DomainJsonHelper.GetBuilder();
            dto.WriteTo(sb);
            string json = sb.ToString();
            if (json != m_LastMobilizationJson)
            {
                m_LastMobilizationJson = json;
                PublishJsonWhenComplete(MobilizationState, NoSourceChecks, () => json);
            }
        }

        private void FillEligibility(ref MobilizationDto dto, MobilizationStateSingleton state, bool callToArmsOnCooldown, bool conscriptionOnCooldown)
        {
            bool inCrisis = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                && actSingleton.TryRequireAct(Act.Crisis, out _);
            bool callToArmsPending = IsRequestPending(MobilizationActionType.CallToArms)
                || RequestResultBridge.Get(RequestResultBridge.CallToArms).Status == RequestStatus.Pending;

            dto.CanCallToArms = MobilizationEligibility.CanCallToArms(
                inCrisis,
                state.Casualties,
                callToArmsOnCooldown,
                out var callToArmsReason);
            if (callToArmsPending)
            {
                dto.CanCallToArms = false;
                dto.CallToArmsLockedReasonId = ReasonIds.MobRequestPending;
            }
            else
            {
                dto.CallToArmsLockedReasonId = callToArmsReason;
            }

            if (!inCrisis)
            {
                dto.CanToggleConscription = false;
                dto.ConscriptionLockedReasonId = ReasonIds.MobNotInCrisis;
                return;
            }

            if (m_HasPendingConscriptionTarget)
            {
                dto.CanToggleConscription = false;
                dto.ConscriptionLockedReasonId = ReasonIds.MobRequestPending;
                return;
            }

            // Re-activation is gated by the cooldown; deactivation is always allowed
            // (the player can still drop conscription and its happiness penalty).
            dto.CanToggleConscription = MobilizationEligibility.CanToggleConscription(
                reactivating: !state.IsConscriptionActive,
                conscriptionOnCooldown,
                out var conscriptionReason);
            dto.ConscriptionLockedReasonId = conscriptionReason;
        }

        private TriggerOutcome OnToggleConscription(in ScenarioGuard guard)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Conscription toggle rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            if (m_HasPendingConscriptionTarget)
            {
                return TriggerOutcome.Reject(ReasonIds.MobRequestPending);
            }

            // NO_MIGRATE: trigger rejects when Mobilization state is absent.
            if (!m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var state))
            {
                return TriggerOutcome.Reject(ReasonIds.MobStateUnavailable);
            }

            bool targetActive = !state.IsConscriptionActive;
            var action = state.IsConscriptionActive
                ? MobilizationActionType.DeactivateConscription
                : MobilizationActionType.ActivateConscription;

            m_HasPendingConscriptionTarget = true;
            m_PendingConscriptionTarget = targetActive;
            Log.Info($"Conscription toggle requested: {action}");
            return CreateMobilizationRequest(action);
        }

        private TriggerOutcome OnCallToArms(in ScenarioGuard guard)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("CallToArms rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            if (IsRequestPending(MobilizationActionType.CallToArms)
                || RequestResultBridge.Get(RequestResultBridge.CallToArms).Status == RequestStatus.Pending)
            {
                return TriggerOutcome.Reject(ReasonIds.MobRequestPending);
            }

            // NO_MIGRATE: trigger rejects when Mobilization state is absent.
            if (!m_MobilizationQuery.TryGetSingleton<MobilizationStateSingleton>(out var state))
            {
                return TriggerOutcome.Reject(ReasonIds.MobStateUnavailable);
            }

            string reasonId;
            if (!MobilizationEligibility.CanCallToArms(true, state.Casualties, IsCallToArmsOnCooldown(state), out reasonId))
            {
                return TriggerOutcome.RejectRuntime(reasonId);
            }

            Log.Info("CallToArms requested");
            return CreateMobilizationRequest(MobilizationActionType.CallToArms);
        }

        private TriggerOutcome CreateMobilizationRequest(MobilizationActionType action)
        {
            // No pause-gate here: both callers (OnToggleConscription, OnCallToArms) reject on
            // pause before reaching this synchronous sink, and OnToggleConscription sets its
            // pending latch only after its own pause-gate — gating again here would be dead.
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new MobilizationRequest
            {
                Action = action
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created MobilizationRequest: action={action}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private static bool IsCallToArmsOnCooldown(MobilizationStateSingleton state)
        {
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null) return state.IsCallToArmsOnCooldown;
            return timeProvider.Current.TotalGameHours < state.CallToArmsCooldownEndHour;
        }

        private static bool IsConscriptionReactivationOnCooldown(MobilizationStateSingleton state)
        {
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null) return state.IsConscriptionReactivationOnCooldown;
            return timeProvider.Current.TotalGameHours < state.ConscriptionCooldownEndHour;
        }

        private bool IsRequestPending(MobilizationActionType action)
        {
            if (m_MobilizationRequestQuery.IsEmptyIgnoreFilter)
                return false;

            using var requests = m_MobilizationRequestQuery.ToComponentDataArray<MobilizationRequest>(Allocator.Temp);
            foreach (var request in requests)
            {
                if (request.Action == action)
                    return true;
            }

            return false;
        }

        private void RefreshPendingConscriptionTargetFromLiveRequests()
        {
            if (m_MobilizationRequestQuery.IsEmptyIgnoreFilter)
                return;

            using var requests = m_MobilizationRequestQuery.ToComponentDataArray<MobilizationRequest>(Allocator.Temp);
            foreach (var request in requests)
            {
                switch (request.Action)
                {
                    case MobilizationActionType.ActivateConscription:
                        m_HasPendingConscriptionTarget = true;
                        m_PendingConscriptionTarget = true;
                        return;
                    case MobilizationActionType.DeactivateConscription:
                        m_HasPendingConscriptionTarget = true;
                        m_PendingConscriptionTarget = false;
                        return;
                    default:
                        break;
                }
            }
        }
    }
}
