using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Services.Countermeasures;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Domains.Countermeasures.Logic;
using CivicSurvival.Core.UI;
using static CivicSurvival.Core.UI.B;
using CountermeasuresPhaseEnum = CivicSurvival.Core.Types.CountermeasuresPhase;
using CivicSurvival.Core.Attributes;
using Unity.Collections;

namespace CivicSurvival.Domains.Countermeasures.UI
{
    /// <summary>
    /// UI system for countermeasures data.
    /// ECS-Pure: Reads directly from CountermeasuresCoreFsm + CmInvestigationState + CmProtestState singletons.
    ///
    /// Migrated from CountermeasuresUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQuery, RequireForUpdate, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [TransientConsumerReconcile(typeof(CountermeasureChoiceRequest), ReconcileMode.NoDurableSideEffect)]
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
    public partial class CountermeasuresUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_CountermeasuresQuery;
        private EntityQuery m_RequestQuery;
        private EntityQuery m_WalletQuery;
        private EntityQuery m_SanctionsQuery;
        private bool m_ChoiceRequestPending;
        private int m_ChoiceRequestCreatedFrame = -1;
        private FixedString128Bytes m_LastChoiceResultValue;
        private string m_LastChoiceResultText = "";
        private FixedString128Bytes m_CurrentJournalistValue;
        private string m_CurrentJournalistText = "";
        private bool m_ArrestedDismissed;
        [System.NonSerialized]
        private bool m_ArrestedModalRequested;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_CountermeasuresQuery = GetEntityQuery(
                ComponentType.ReadOnly<CountermeasuresCoreFsm>(),
                ComponentType.ReadOnly<CmInvestigationState>(),
                ComponentType.ReadOnly<CmPoliceState>(),
                ComponentType.ReadOnly<CmProtestState>());
            m_RequestQuery = GetEntityQuery(ComponentType.ReadOnly<CountermeasureChoiceRequest>());
            m_WalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());
            m_SanctionsQuery = GetEntityQuery(ComponentType.ReadOnly<CivicSurvival.Core.Components.CrossDomain.DonorSanctionsSingleton>());

            RequireForUpdate(m_CountermeasuresQuery);

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(B.CountermeasuresState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(MakeInvestigationChoice, FeatureIds.Countermeasures, RequestResultBridge.CountermeasureChoice, OnMakeInvestigationChoice);
            Triggers.Add<int>(MakePoliceChoice, FeatureIds.Countermeasures, RequestResultBridge.CountermeasureChoice, OnMakePoliceChoice);
            Triggers.Add(DismissArrested, FeatureIds.Countermeasures, OnDismissArrested);
        }

        protected override void OnPanelUpdate()
        {
            if (!m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var core))
                return;
            if (!m_CountermeasuresQuery.TryGetSingleton<CmInvestigationState>(out var inv))
                return;
            if (!m_CountermeasuresQuery.TryGetSingleton<CmPoliceState>(out var police))
                return;
            if (!m_CountermeasuresQuery.TryGetSingleton<CmProtestState>(out var protest))
                return;

            if (m_ChoiceRequestPending &&
                UnityEngine.Time.frameCount > m_ChoiceRequestCreatedFrame &&
                m_RequestQuery.IsEmpty)
            {
                m_ChoiceRequestPending = false;
            }

            bool isArrested = core.CurrentPhase == CountermeasuresPhaseEnum.Arrested;
            if (!isArrested)
            {
                m_ArrestedDismissed = false;
                if (m_ArrestedModalRequested)
                {
                    m_ArrestedModalRequested = false;
                    ModalCoordinator.Instance.Dismiss("Arrested");
                }
            }
            else if (!m_ArrestedDismissed && !m_ArrestedModalRequested)
            {
                _ = ModalCoordinator.Instance.TryShow("Arrested", new ArrestedModalPayloadDto
                {
                    ChargesCount = core.ChargesCount,
                    AssetsSeizedSnapshot = core.ArrestedAssetsSeized,
                    WalletBalanceAfter = core.ArrestedWalletAfter,
                    LastChoiceResult = GetCachedString(ref m_LastChoiceResultValue, ref m_LastChoiceResultText, core.LastChoiceResult)
                });
                m_ArrestedModalRequested = true;
            }

            var choiceType = CountermeasuresHelper.GetChoiceType(core.CurrentPhase);
            int baseBribeCost = CountermeasuresHelper.GetBaseBribeCost(core, inv);
            float sanctionsMarkup = m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)
                ? wallet.SanctionsMarkup
                : 0f;
            int bribeCost = CountermeasuresHelper.GetBribeCost(core, inv, sanctionsMarkup);
            var bribeAvailability = ActionAvailabilityField.Reject(string.Empty, bribeCost);
            if (TryGetBribeActionKey(core.CurrentPhase, out var bribeKey))
            {
                var bribeGate = ActionGate.Resolve(bribeKey, BuildShadowActionContext(baseBribeCost));
                bribeAvailability = bribeGate.EffectiveCost > 0
                    ? bribeGate
                    : bribeGate.WithEffectiveCost(bribeCost);
            }

            var dto = new CountermeasuresDto
            {
                CorruptionScore = (int)core.CorruptionScore,
                Heat = (int)core.Heat,
                HeatLevel = CountermeasuresHelper.GetHeatLevel(core.Heat),
                CountermeasuresPhase = CountermeasuresHelper.GetPhaseName(core.CurrentPhase),
                InvestigationProgress = inv.Progress,
                ChargesCount = GetDisplayCharges(core, police),
                ProtestCount = protest.ActiveProtests,
                ChoiceRequired = CountermeasuresHelper.ChoiceRequired(core.CurrentPhase),
                ChoiceType = (int)choiceType,
                BaseBribeCost = baseBribeCost,
                BribeCost = bribeCost,
                BribeAvailability = bribeAvailability,
                LastChoiceResult = GetCachedString(ref m_LastChoiceResultValue, ref m_LastChoiceResultText, core.LastChoiceResult),
                CurrentJournalist = GetCachedString(ref m_CurrentJournalistValue, ref m_CurrentJournalistText, inv.Journalist),
                IsArrested = isArrested,
                ArrestedAssetsSeized = core.ArrestedAssetsSeized,
                ArrestedWalletAfter = core.ArrestedWalletAfter,
                BribeRiskWarning = GetBribeRiskWarning(core.CurrentPhase),
                SanctionsSuppressingCorruption = m_SanctionsQuery.TryGetSingleton<CivicSurvival.Core.Components.CrossDomain.DonorSanctionsSingleton>(out var sanctions) && sanctions.SanctionsActive,
                LastChoiceRequestResultJson = RequestResultBridge.Get(RequestResultBridge.CountermeasureChoice).ToJson()
            };

            PublishWhenComplete(B.CountermeasuresState, NoSourceChecks, () => dto);
        }

        private ActionContext BuildShadowActionContext(long proposedCost)
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                false,
                GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);

            if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
                ctx = ctx.WithWallet(wallet);

            return ctx.WithCost(proposedCost);
        }

        private static bool TryGetBribeActionKey(CountermeasuresPhaseEnum phase, out ActionKey key)
        {
            switch (phase)
            {
                case CountermeasuresPhaseEnum.WaitingForInvestigationChoice:
                    key = ActionKey.InvestigationBribe;
                    return true;
                case CountermeasuresPhaseEnum.WaitingForPoliceChoice:
                    key = ActionKey.PoliceBribe;
                    return true;
                default:
                    key = default;
                    return false;
            }
        }

        private string GetBribeRiskWarning(CountermeasuresPhaseEnum phase)
        {
            switch (CountermeasuresHelper.GetChoiceType(phase))
            {
                case CountermeasureChoiceUiType.Investigation:
                    return "RISK_BRIBE_INVESTIGATION_WARNING";
                case CountermeasureChoiceUiType.Police:
                    return "RISK_BRIBE_POLICE_WARNING";
                case CountermeasureChoiceUiType.None:
                    return "";
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(phase), phase, "Unknown countermeasure choice type");
            }
        }

        private void OnDismissArrested()
        {
            m_ArrestedDismissed = true;
            m_ArrestedModalRequested = false;
            ModalCoordinator.Instance.Dismiss("Arrested");
        }

        private static int GetDisplayCharges(CountermeasuresCoreFsm core, CmPoliceState police)
        {
            return core.CurrentPhase == CountermeasuresPhaseEnum.PoliceInvestigation ||
                   core.CurrentPhase == CountermeasuresPhaseEnum.WaitingForPoliceChoice
                ? police.ChargesCount
                : core.ChargesCount;
        }

        private static string GetCachedString(
            ref FixedString128Bytes cachedValue,
            ref string cachedText,
            FixedString128Bytes currentValue)
        {
            if (!cachedValue.Equals(currentValue))
            {
                cachedValue = currentValue;
                cachedText = currentValue.ToString();
            }

            return cachedText;
        }

        private TriggerOutcome OnMakeInvestigationChoice(int choice)
        {
            // Cast to the enum before IsDefined: InvestigationChoice's underlying type is
            // byte, and Enum.IsDefined throws ArgumentException when the value's type (int)
            // differs from the enum underlying type.
            var choiceEnum = (InvestigationChoice)choice;
            if (!System.Enum.IsDefined(typeof(InvestigationChoice), choiceEnum))
            {
                Log.Warn($"Invalid investigation choice from UI: {choice}");
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }
            if (choiceEnum == InvestigationChoice.None)
            {
                Log.Warn("Invalid empty investigation choice from UI");
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Investigation choice rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            if (!CanCreateChoiceRequest(CountermeasureChoiceType.Investigation, CountermeasuresPhaseEnum.WaitingForInvestigationChoice))
            {
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new CountermeasureChoiceRequest
            {
                ChoiceType = CountermeasureChoiceType.Investigation,
                ChoiceValue = choice
            });
            m_ChoiceRequestPending = true;
            m_ChoiceRequestCreatedFrame = UnityEngine.Time.frameCount;
            if (Log.IsDebugEnabled) Log.Debug($"Created CountermeasureChoiceRequest: Investigation={choiceEnum}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnMakePoliceChoice(int choice)
        {
            // Cast to the enum before IsDefined: PoliceChoice's underlying type is byte
            // (same reason as InvestigationChoice above) — an int value would throw.
            var choiceEnum = (PoliceChoice)choice;
            if (!System.Enum.IsDefined(typeof(PoliceChoice), choiceEnum))
            {
                Log.Warn($"Invalid police choice from UI: {choice}");
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }
            if (choiceEnum == PoliceChoice.None)
            {
                Log.Warn("Invalid empty police choice from UI");
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Police choice rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            if (!CanCreateChoiceRequest(CountermeasureChoiceType.Police, CountermeasuresPhaseEnum.WaitingForPoliceChoice))
            {
                return TriggerOutcome.Reject(ReasonIds.CounterChoiceNotAvailable);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new CountermeasureChoiceRequest
            {
                ChoiceType = CountermeasureChoiceType.Police,
                ChoiceValue = choice
            });
            m_ChoiceRequestPending = true;
            m_ChoiceRequestCreatedFrame = UnityEngine.Time.frameCount;
            if (Log.IsDebugEnabled) Log.Debug($"Created CountermeasureChoiceRequest: Police={choiceEnum}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private bool CanCreateChoiceRequest(CountermeasureChoiceType choiceType, CountermeasuresPhaseEnum requiredPhase)
        {
            if (m_ChoiceRequestPending || !m_RequestQuery.IsEmpty)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Ignored duplicate {choiceType} choice request");
                return false;
            }

            if (!m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var core) ||
                core.CurrentPhase != requiredPhase)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Ignored {choiceType} choice request outside {requiredPhase}");
                return false;
            }

            return true;
        }
    }
#pragma warning restore CIVIC098
}
