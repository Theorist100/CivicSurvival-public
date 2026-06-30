using System;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Interfaces.Domain.AirDefense;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Domains.Diplomacy.Data;
using CivicSurvival.Domains.Diplomacy.Logic;
using CivicSurvival.Domains.Diplomacy.Systems;
using CivicSurvival.Core.UI;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.Diplomacy.UI
{
    /// <summary>
    /// UI system for donor conference data.
    /// ECS-Pure: Reads from ShockStateSingleton and CountermeasuresState singletons.
    ///
    /// Migrated from DonorConferenceUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class DonorConferenceUISystem : CivicUIPanelSystem
    {
        private const int MAX_GENERATORS = 20;
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_ShockQuery;
        private bool m_LoggedMissingConferenceSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_ShockQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShockStateSingleton>());
            Log.Info("Created");
        }

        // Track dialog state for trigger handlers

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(DonorState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add(OpenDonorConference, FeatureIds.Diplomacy, RequestResultBridge.DonorDialog, OnOpenConference);
            Triggers.Add(CloseDonorConference, FeatureIds.Diplomacy, RequestResultBridge.DonorDialog, OnCloseConference);
            Triggers.Add(SelectDonorFunds, FeatureIds.Diplomacy, RequestResultBridge.DonorSelection, OnSelectFunds);
            Triggers.Add(SelectDonorPower, FeatureIds.Diplomacy, RequestResultBridge.DonorSelection, OnSelectPower);
            Triggers.Add(SelectDonorDefense, FeatureIds.Diplomacy, RequestResultBridge.DonorSelection, OnSelectDefense);
        }

        protected override void OnPanelUpdate()
        {
            if (DonorConferenceSystem.Instance == null)
            {
                if (!m_LoggedMissingConferenceSystem)
                {
                    Log.Warn("DonorConferenceSystem unavailable — donor UI state withheld");
                    m_LoggedMissingConferenceSystem = true;
                }
                PublishLockedState("unavailable", ReasonIds.DonorTrustSourceUnavailable);
                return;
            }
            m_LoggedMissingConferenceSystem = false;

            var state = DonorConferenceSystem.State;

            // Skip expensive aid matrix computation before Crisis act
            var currentAct = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                ? actSingleton.CurrentAct
                : Act.PreWar;
            if (currentAct < Act.Crisis && !DonorConferenceSystem.DialogActive)
            {
                PublishLockedState("too_early", ReasonId.None, state);
                return;
            }

            if (!DonorConferenceSystem.Instance.TryGetTrustInputs(out var corruption, out var scandalPenalty, out var trustReason))
            {
                PublishLockedState(DonorConferenceSystem.GetStatusString(), trustReason, state);
                return;
            }

            int trust = DonorAidCalculator.GetTrustIndex(corruption, scandalPenalty);
            var trustLevel = DonorConferenceSystem.ResolveTrustLevel(corruption, scandalPenalty);

            var shockTier = AidTier.DeepConcern;
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shockState))
            {
                shockTier = shockState.CurrentTier;
            }

            // Single source of truth for aid amounts (shock + trust → same values UI matrix shows)
            var aidPackage = AidMatrixCalculator.Calculate(shockTier, trustLevel);
            bool hasAvailableGenerators = aidPackage.Generators > 0 && state.ActiveGenerators < MAX_GENERATORS;
            var airDefenseCredits = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullAirDefenseCreditsReader.Instance);
            bool airDefenseAvailable = airDefenseCredits.IsAvailable;
            bool donorPatriotCreditCapReached = airDefenseCredits.IsDonorPatriotCreditCapReached;

            bool fundsMatrix = DonorAidCalculator.IsAidAvailable(trustLevel, DonorAidType.Funds, shockTier);
            bool powerMatrix = DonorAidCalculator.IsAidAvailable(trustLevel, DonorAidType.Power, shockTier);
            bool defenseMatrix = DonorAidCalculator.IsAidAvailable(trustLevel, DonorAidType.Defense, shockTier);

            bool fundsAvailable = DonorEligibility.CanDonateFunds(fundsMatrix, trustLevel, out var fundsReason);
            bool powerAvailable = DonorEligibility.CanProvidePower(powerMatrix, hasAvailableGenerators, trustLevel, shockTier, out var powerReason);
            bool defenseAvailable = DonorEligibility.CanProvideDefense(defenseMatrix, airDefenseAvailable, donorPatriotCreditCapReached, trustLevel, shockTier, out var defenseReason);

            var dto = new DonorDto
            {
                DonorUsesRemaining = state.UsesRemaining,
                DonorCooldownDays = (int)Math.Ceiling(state.CooldownDaysRemaining),
                DonorStatus = DonorConferenceSystem.GetStatusString(),
                TrustIndex = trust,
                ScandalPenalty = scandalPenalty,
                DonorExpectedAid = DonorAidCalculator.GetExpectedAidDescription(trustLevel, in aidPackage),
                DonorDialogActive = DonorConferenceSystem.DialogActive,
                ProducerReady = trustReason.IsEmpty,
                TrustLocked = false,
                ProducerReasonId = trustReason.ToString(),
                DonorFundsAvailable = fundsAvailable,
                DonorPowerAvailable = powerAvailable,
                DonorDefenseAvailable = defenseAvailable,
                DonorFundsLockedReasonId = fundsReason,
                DonorPowerLockedReasonId = powerReason,
                DonorDefenseLockedReasonId = defenseReason,
                DonorFundsAmount = aidPackage.Funds,
                DonorGeneratorCount = aidPackage.Generators,
                DonorGeneratorMW = aidPackage.GeneratorMW,
                DonorPatriotDays = aidPackage.PatriotDays,
                DonorActiveGenerators = state.ActiveGenerators,
                SanctionsActive = state.SanctionsActive,
                SanctionDaysRemaining = (int)Math.Ceiling(state.SanctionDaysRemaining),
                SanctionTradePenalty = (int)Math.Round(state.TradePenalty * 100),
                DonorDialogRequestJson = RequestResultBridge.Get(RequestResultBridge.DonorDialog).ToJson(),
                DonorSelectionRequestJson = RequestResultBridge.Get(RequestResultBridge.DonorSelection).ToJson()
            };

            FillAidMatrix(ref dto, aidPackage, shockTier);

            PublishWhenComplete(DonorState, NoSourceChecks, () => dto);
        }

        private void PublishLockedState(string status, ReasonId reasonId)
            => PublishLockedState(status, reasonId, null);

        private void PublishLockedState(string status, ReasonId reasonId, DonorConferenceStateData? state)
        {
            var donorState = state ?? default;
            var dto = new DonorDto
            {
                DonorUsesRemaining = donorState.UsesRemaining,
                DonorCooldownDays = (int)Math.Ceiling(donorState.CooldownDaysRemaining),
                DonorStatus = status,
                DonorDialogActive = false,
                ProducerReady = reasonId.IsEmpty,
                TrustLocked = !reasonId.IsEmpty,
                ProducerReasonId = reasonId.ToString(),
                DonorFundsLockedReasonId = reasonId.IsEmpty ? "" : reasonId,
                DonorPowerLockedReasonId = reasonId.IsEmpty ? "" : reasonId,
                DonorDefenseLockedReasonId = reasonId.IsEmpty ? "" : reasonId,
                DonorActiveGenerators = donorState.ActiveGenerators,
                SanctionsActive = donorState.SanctionsActive,
                SanctionDaysRemaining = (int)Math.Ceiling(donorState.SanctionDaysRemaining),
                SanctionTradePenalty = (int)Math.Round(donorState.TradePenalty * 100),
                DonorDialogRequestJson = RequestResultBridge.Get(RequestResultBridge.DonorDialog).ToJson(),
                DonorSelectionRequestJson = RequestResultBridge.Get(RequestResultBridge.DonorSelection).ToJson()
            };

            PublishWhenComplete(DonorState, NoSourceChecks, () => dto);
        }

        private void FillAidMatrix(ref DonorDto dto, FilteredAidPackage package, AidTier shockTier)
        {

            dto.AidTierId = (int)package.ShockTier;
            dto.AidFundsOffered = GetOfferedFunds(shockTier);
            dto.AidFundsAccessible = package.Funds;
            dto.PatriotOffered = shockTier == AidTier.GlobalShock;
            dto.PatriotBlocked = package.PatriotOfferedButBlocked;
            dto.TrustMessageId = (int)package.TrustMessage;
            dto.BlockedReasonId = (int)package.BlockedReason;
            dto.HasBlockedItems = package.HasBlockedItems;
        }

        private int GetOfferedFunds(AidTier tier)
        {
            var aid = BalanceConfig.Current.Aid;
            return tier switch
            {
                AidTier.None => 0,
                AidTier.DeepConcern => aid.DeepConcernFunds,
                AidTier.Headlines => aid.HeadlinesFunds,
                AidTier.GlobalShock => aid.GlobalShockFunds,
                _ => LogUnknownTierFunds(tier)
            };
        }

        private static int LogUnknownTierFunds(AidTier tier)
        {
            Mod.Log.Warn($"Unknown AidTier in GetOfferedFunds: {tier} — returning 0");
            return 0;
        }

        private TriggerOutcome OnOpenConference()
        {
            Log.Info("UI Trigger: openDonorConference");
            return CreateDialogRequest(DonorDialogAction.Open);
        }

        private TriggerOutcome OnCloseConference()
        {
            Log.Info("UI Trigger: closeDonorConference");
            return CreateDialogRequest(DonorDialogAction.Close);
        }

        private TriggerOutcome OnSelectFunds()
        {
            Log.Info("UI Trigger: selectDonorFunds");
            return CreateSelectionRequest(DonorSelectionType.Funds);
        }

        private TriggerOutcome OnSelectPower()
        {
            Log.Info("UI Trigger: selectDonorPower");
            return CreateSelectionRequest(DonorSelectionType.Power);
        }

        private TriggerOutcome OnSelectDefense()
        {
            Log.Info("UI Trigger: selectDonorDefense");
            return CreateSelectionRequest(DonorSelectionType.Defense);
        }

        private TriggerOutcome CreateDialogRequest(DonorDialogAction action)
        {
            if (action == DonorDialogAction.Open && TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("DonorDialogRequest rejected: resource pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new DonorDialogRequest
            {
                Action = action
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created DonorDialogRequest: action={action}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome CreateSelectionRequest(DonorSelectionType selectionType)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("DonorSelectionRequest rejected: budget/resource pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new DonorSelectionRequest
            {
                SelectionType = selectionType
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created DonorSelectionRequest: type={selectionType}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }
    }
}
