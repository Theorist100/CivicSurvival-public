using System;
using System.Collections.Generic;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;
using Random = Unity.Mathematics.Random;

namespace CivicSurvival.Core.Services.Countermeasures
{
    /// <summary>
    /// Processes player choices for Investigation and Police phases.
    /// Extracted from CountermeasuresUpdateSystem to reduce cognitive density.
    ///
    /// Side-effects: EventBus, CmWalletOps, IShadowReputationService.
    /// Caller: CountermeasuresUpdateSystem (sole caller)
    /// </summary>
    public sealed class CmChoiceProcessor
    {
        private static readonly LogContext Log = new("Countermeasures");

        private readonly IEventBus? m_EventBus;
        private readonly IShadowReputationService m_ReputationService;
        private readonly CmWalletOps m_Wallet;
        private readonly Func<int> m_GetGameDay;
        private readonly Func<Act> m_GetCurrentAct;

        public CmChoiceProcessor(
            IEventBus? eventBus,
            IShadowReputationService reputationService,
            CmWalletOps wallet,
            Func<int> getGameDay,
            Func<Act> getCurrentAct)
        {
            m_EventBus = eventBus;
            m_ReputationService = reputationService;
            m_Wallet = wallet;
            m_GetGameDay = getGameDay;
            m_GetCurrentAct = getCurrentAct;
        }

        // ============================================================================
        // INVESTIGATION CHOICES
        // ============================================================================

        public RequestStatus ProcessInvestigationChoice(
            InvestigationChoice choice,
            ref CountermeasuresCoreFsm core,
            ref CmInvestigationState inv,
            in RequestMeta requestMeta,
            ref bool hasPendingInvestigationBribe,
            out string reasonId)
        {
            reasonId = ReasonIds.CountermeasuresInvalidChoice;
            if (core.CurrentPhase != CountermeasuresPhase.WaitingForInvestigationChoice)
            {
                Log.Warn($"[Countermeasures] Investigation choice in wrong phase: {core.CurrentPhase}");
                core.LastChoiceResult = default;
                return RequestStatus.Failed;
            }

            if (choice == InvestigationChoice.None)
            {
                core.LastChoiceResult = default;
                return RequestStatus.Failed;
            }

            if (hasPendingInvestigationBribe && choice != InvestigationChoice.Bribe)
            {
                core.LastChoiceResult = new FixedString128Bytes(ReasonIds.CounterChoiceNotAvailable);
                reasonId = ReasonIds.CounterChoiceNotAvailable;
                return RequestStatus.Failed;
            }

            var cfg = BalanceConfig.Current.Countermeasures;

            RequestStatus status;
            switch (choice)
            {
                case InvestigationChoice.Bribe:
                    status = HandleInvestigationBribe(ref core, ref inv, requestMeta, ref hasPendingInvestigationBribe, out reasonId);
                    break;
                case InvestigationChoice.Censor:
                    status = HandleInvestigationCensor(ref core, ref inv, cfg) ? RequestStatus.Success : RequestStatus.Failed;
                    break;
                case InvestigationChoice.Wait:
                    status = HandleInvestigationWait(ref core, ref inv, cfg) ? RequestStatus.Success : RequestStatus.Failed;
                    break;
                case InvestigationChoice.Confess:
                    status = HandleInvestigationConfess(ref core, ref inv, cfg) ? RequestStatus.Success : RequestStatus.Failed;
                    break;
                default:
                    Log.Warn($"[Countermeasures] Unknown investigation choice: {(int)choice}");
                    core.LastChoiceResult = default;
                    return RequestStatus.Failed;
            }

            // No randomness is consumed here. The journalist-bribe roll lives in
            // ResolvePaidInvestigationBribe (two-phase: queue the retained deduct in this
            // method, roll + write inv.RngState back on resolve). inv.RngState stays untouched.

            if (status != RequestStatus.Pending)
            {
                m_EventBus?.SafePublish(
                    new CountermeasuresChoiceEvent("investigation", EnumName<InvestigationChoice>.Get(choice), core.LastChoiceResult.ToString()),
                    "CountermeasuresUpdateSystem");
            }

            if (status == RequestStatus.Success)
                reasonId = "";

            return status;
        }

        private RequestStatus HandleInvestigationBribe(
            ref CountermeasuresCoreFsm core,
            ref CmInvestigationState inv,
            in RequestMeta requestMeta,
            ref bool hasPendingInvestigationBribe,
            out string reasonId)
        {
            reasonId = ReasonIds.CountermeasuresInvalidChoice;
            int baseCost = inv.BribeCost;

            if (hasPendingInvestigationBribe)
            {
                core.LastChoiceResult = new FixedString128Bytes(ReasonIds.CounterChoiceNotAvailable);
                reasonId = ReasonIds.CounterChoiceNotAvailable;
                return RequestStatus.Failed;
            }

            if (baseCost <= 0)
            {
                Log.Error("[Countermeasures] BribeCost is 0 — likely corrupted state, rejecting bribe");
                core.LastChoiceResult = new FixedString128Bytes("Bribe cost is invalid!");
                reasonId = ReasonIds.CounterChoiceNotAvailable;
                return RequestStatus.Failed;
            }

            var gate = m_Wallet.ResolveAction(ActionKey.InvestigationBribe, m_GetCurrentAct(), baseCost);
            if (!gate.CanRun)
            {
                core.LastChoiceResult = new FixedString128Bytes(gate.LockedReasonId);
                reasonId = gate.LockedReasonId;
                Log.Info($"[Countermeasures] Investigation bribe blocked: {gate.LockedReasonId}");
                return RequestStatus.Failed;
            }

            if (!m_Wallet.TryQueueRetainedDeduct(baseCost, requestMeta, out _, out long effectiveCost))
            {
                core.LastChoiceResult = new FixedString128Bytes("Not enough money in Reserve Fund!");
                reasonId = ReasonIds.MarketInsufficientFunds;
                return RequestStatus.Failed;
            }

            core.LastChoiceResult = new FixedString128Bytes($"Bribe payment pending: ${effectiveCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}");
            hasPendingInvestigationBribe = true;
            return RequestStatus.Pending;
        }

        public bool ResolvePaidInvestigationBribe(
            ref CountermeasuresCoreFsm core,
            ref CmInvestigationState inv,
            long effectiveCost,
            out string reasonId)
        {
            reasonId = ReasonIds.CountermeasuresInvalidChoice;
            if (core.CurrentPhase != CountermeasuresPhase.WaitingForInvestigationChoice)
            {
                core.LastChoiceResult = new FixedString128Bytes(ReasonIds.CounterChoiceNotAvailable);
                reasonId = ReasonIds.CounterChoiceNotAvailable;
                return false;
            }

            var cfg = BalanceConfig.Current.Countermeasures;
            var rng = new Random(inv.RngState);
            if (rng.NextFloat() < cfg.JournalistBetrayalChance)
            {
                Log.Info("[Countermeasures] Journalist bribe backfired!");
                core.LastChoiceResult = new FixedString128Bytes($"Journalist took ${effectiveCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} and published anyway!");

                core.CurrentPhase = CountermeasuresPhase.ArticlePublished;
                core.ChargesCount = Math.Max(1, (int)Math.Round(core.CorruptionScore / Math.Max(cfg.ChargeDivisorNormal, 1))) + cfg.ChargesIncrement;
                // R9-H03 FIX: Set cooldown so police doesn't start on the very next tick.
                // Without this, player loses bribe money AND gets immediate police (double punishment).
                core.NextEventHour = core.GameHour + cfg.EventCooldownHours;
                ResetInvestigationState(ref inv);

                m_EventBus?.SafePublish(new CorruptionNarrativeEvent(CorruptionNarrativeEventType.ArticlePublished, ChargesCount: core.ChargesCount));
                inv.RngState = rng.state;
                PublishInvestigationChoiceEvent(core.LastChoiceResult.ToString());
                reasonId = "";
                return true;
            }

            Log.Info("[Countermeasures] Journalist bribe successful");
            core.LastChoiceResult = new FixedString128Bytes($"Journalist accepted ${effectiveCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}. Investigation dropped.");
            core.CurrentPhase = CountermeasuresPhase.Suspicion;
            CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);
            m_ReputationService.OnSchemeSuccessful();

            PublishInvestigationStopped(inv.Journalist.ToString(), "Bribe", effectiveCost.ToString());
            ResetInvestigationState(ref inv);

            inv.RngState = rng.state;
            PublishInvestigationChoiceEvent(core.LastChoiceResult.ToString());
            reasonId = "";
            return true;
        }

        public void PublishInvestigationBribeFailed(string result)
            => PublishInvestigationChoiceEvent(result);

        private void PublishInvestigationChoiceEvent(string result)
        {
            m_EventBus?.SafePublish(
                new CountermeasuresChoiceEvent("investigation", EnumName<InvestigationChoice>.Get(InvestigationChoice.Bribe), result),
                "CountermeasuresUpdateSystem");
        }

        private bool HandleInvestigationCensor(ref CountermeasuresCoreFsm core, ref CmInvestigationState inv, CountermeasuresConfig cfg)
        {
            Log.Info("[Countermeasures] Censorship applied");
            core.LastChoiceResult = new FixedString128Bytes("Article censored. Journalist will remember this.");
            core.CurrentPhase = CountermeasuresPhase.Suspicion;
            CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);
            m_ReputationService.OnSchemeSuccessful();

            PublishInvestigationStopped(inv.Journalist.ToString(), "Censor");

            ResetInvestigationState(ref inv);
            return true;
        }

        private bool HandleInvestigationWait(ref CountermeasuresCoreFsm core, ref CmInvestigationState inv, CountermeasuresConfig cfg)
        {
            Log.Info("[Countermeasures] Player chose to wait - article published");
            core.LastChoiceResult = new FixedString128Bytes("Article published. Police are interested.");
            core.CurrentPhase = CountermeasuresPhase.ArticlePublished;
            core.ChargesCount = Math.Max(1, (int)Math.Round(core.CorruptionScore / Math.Max(cfg.ChargeDivisorNormal, 1)));
            // R9-H03 FIX: Set cooldown before police can start (same as betrayal path).
            core.NextEventHour = core.GameHour + cfg.EventCooldownHours;

            m_EventBus?.SafePublish(new CorruptionNarrativeEvent(CorruptionNarrativeEventType.ArticlePublished, ChargesCount: core.ChargesCount));
            ResetInvestigationState(ref inv);
            return true;
        }

        private bool HandleInvestigationConfess(ref CountermeasuresCoreFsm core, ref CmInvestigationState inv, CountermeasuresConfig cfg)
        {
            Log.Info("[Countermeasures] Player confessed");
            core.LastChoiceResult = new FixedString128Bytes("You confessed. Article is softer, no police.");
            core.CurrentPhase = CountermeasuresPhase.Suspicion;
            CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);

            PublishInvestigationStopped(inv.Journalist.ToString(), "Confess");

            ResetInvestigationState(ref inv);
            return true;
        }

        // ============================================================================
        // POLICE CHOICES
        // ============================================================================

        public bool ProcessPoliceChoice(PoliceChoice choice, ref CountermeasuresCoreFsm core, ref CmPoliceState police, EntityCommandBuffer ecb, out string reasonId)
        {
            reasonId = ReasonIds.CountermeasuresInvalidChoice;
            if (core.CurrentPhase != CountermeasuresPhase.WaitingForPoliceChoice)
            {
                Log.Warn($"[Countermeasures] Police choice in wrong phase: {core.CurrentPhase}");
                core.LastChoiceResult = default;
                return false;
            }

            if (choice == PoliceChoice.None)
            {
                core.LastChoiceResult = default;
                return false;
            }

            var cfg = BalanceConfig.Current.Countermeasures;
            var rng = new Random(police.RngState);
            bool rngConsumed = false;

            bool success;
            switch (choice)
            {
                case PoliceChoice.Cooperate:
                    success = HandlePoliceCooperate(ref core, ref police, cfg, ecb);
                    break;
                case PoliceChoice.Destroy:
                    success = HandlePoliceDestroy(ref core, ref police, ref rng, cfg, ecb, out rngConsumed);
                    break;
                case PoliceChoice.Bribe:
                    success = HandlePoliceBribe(ref core, ref police, ref rng, cfg, ecb, out rngConsumed, out reasonId);
                    break;
                default:
                    Log.Warn($"[Countermeasures] Unknown police choice: {(int)choice}");
                    core.LastChoiceResult = default;
                    return false;
            }

            // Only write back RNG state when Destroy/Bribe branches consumed randomness.
            if (rngConsumed)
                police.RngState = rng.state;

            m_EventBus?.SafePublish(
                new CountermeasuresChoiceEvent("police", EnumName<PoliceChoice>.Get(choice), core.LastChoiceResult.ToString()),
                "CountermeasuresUpdateSystem");

            if (success)
                reasonId = "";

            return success;
        }

        private bool HandlePoliceCooperate(ref CountermeasuresCoreFsm core, ref CmPoliceState police, CountermeasuresConfig cfg, EntityCommandBuffer ecb)
        {
            if (core.CorruptionScore < cfg.PoliceCooperateEvidenceThreshold)
            {
                Log.Info("[Countermeasures] Cooperated, case closed - insufficient evidence");
                core.LastChoiceResult = new FixedString128Bytes("Case closed. Insufficient evidence. You're clean... for now.");
                core.CurrentPhase = CountermeasuresPhase.Suspicion;
                CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);
                ResetPoliceState(ref police);
                PublishPoliceInvestigationEnded();
                CmWalletOps.UnfreezeViaEcb(ecb);
                // FIX S4-03: No OnSchemeSuccessful — cooperating with police is not a successful scheme
                return true;
            }

            int charges = Math.Max(1, (int)Math.Round(core.CorruptionScore / Math.Max(cfg.ChargeDivisorPolice, 1)));
            core.LastChoiceResult = new FixedString128Bytes("Evidence found. You are under arrest.");
            ApplyArrest(ref core, ref police, charges, ecb);
            return true;
        }

        private bool HandlePoliceDestroy(ref CountermeasuresCoreFsm core, ref CmPoliceState police, ref Random rng, CountermeasuresConfig cfg, EntityCommandBuffer ecb, out bool rngConsumed)
        {
            rngConsumed = true;
            if (rng.NextFloat() < cfg.EvidenceDestroySuccess)
            {
                Log.Info("[Countermeasures] Evidence destroyed successfully");
                core.LastChoiceResult = new FixedString128Bytes("Evidence destroyed. Case closed.");
                core.CurrentPhase = CountermeasuresPhase.Suspicion;
                CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);
                ResetPoliceState(ref police);
                PublishPoliceInvestigationEnded();
                CmWalletOps.UnfreezeViaEcb(ecb);
                m_ReputationService.OnSchemeSuccessful();
                return true;
            }

            int charges = Math.Max(2, (int)Math.Round(core.CorruptionScore / Math.Max(cfg.ChargeDivisorBribeCaught, 1)));
            core.LastChoiceResult = new FixedString128Bytes("Evidence found in backup! Obstruction of justice added. Double charges.");
            ApplyArrest(ref core, ref police, charges, ecb);
            return true;
        }

        private bool HandlePoliceBribe(ref CountermeasuresCoreFsm core, ref CmPoliceState police, ref Random rng, CountermeasuresConfig cfg, EntityCommandBuffer ecb, out bool rngConsumed, out string reasonId)
        {
            rngConsumed = false;
            reasonId = ReasonIds.CountermeasuresInvalidChoice;
            int baseCost = cfg.PoliceBribeCost;

            // NOTE: No IsFrozen check here — wallet IS frozen during police phase by design.
            // Bribe is the player's escape mechanism from freeze; blocking it would make bribe permanently dead.
            var gate = m_Wallet.ResolveAction(ActionKey.PoliceBribe, m_GetCurrentAct(), baseCost);
            if (!gate.CanRun)
            {
                core.LastChoiceResult = new FixedString128Bytes(gate.LockedReasonId);
                reasonId = gate.LockedReasonId;
                return false;
            }

            // Roll BEFORE deduction: honest cop rejects the bribe — no money taken.
            // (Unlike journalist bribe where the journalist TAKES the money and betrays.)
            rngConsumed = true;
            if (rng.NextFloat() < cfg.PoliceHonestChance)
            {
                int charges = Math.Max(2, (int)Math.Round(core.CorruptionScore / Math.Max(cfg.ChargeDivisorBribeCaught, 1))) + cfg.ChargeBriberyBonus;
                core.LastChoiceResult = new FixedString128Bytes("Detective Shevchenko: 'You just added bribery to your charges.'");
                ApplyArrest(ref core, ref police, charges, ecb);
                return true;
            }

#pragma warning disable CIVIC237 // Main-thread single writer; affordability was checked immediately before RNG, no yield before deduct
            if (!m_Wallet.TryDeductBypassFreeze(baseCost, out _, out long effectiveCost))
#pragma warning restore CIVIC237
            {
                core.LastChoiceResult = new FixedString128Bytes("Not enough money in Reserve Fund!");
                reasonId = ReasonIds.MarketInsufficientFunds;
                return false;
            }

            Log.Info("[Countermeasures] Police bribe successful");
            core.LastChoiceResult = new FixedString128Bytes($"Detective accepted ${effectiveCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}. Case closed.");
            core.CurrentPhase = CountermeasuresPhase.Suspicion;
            CountermeasuresRules.ApplyResolutionCooldown(ref core, cfg);
            ResetPoliceState(ref police);
            PublishPoliceInvestigationEnded();
            CmWalletOps.UnfreezeViaEcb(ecb);
            m_ReputationService.OnSchemeSuccessful();
            return true;
        }

        // ============================================================================
        // SHARED OUTCOME HELPERS
        // ============================================================================

        /// <summary>
        /// Common arrest outcome: phase=Arrested, charges set, police reset, reputation, event, defeat, confiscate.
        /// Used by Cooperate (evidence found), Destroy (failed), Bribe (honest cop).
        /// </summary>
        private void ApplyArrest(ref CountermeasuresCoreFsm core, ref CmPoliceState police, int charges, EntityCommandBuffer ecb)
        {
            Log.Info($"[Countermeasures] Arrested with {charges} charges");
            long assetsSeized = m_Wallet.GetBalance();
            core.CurrentPhase = CountermeasuresPhase.Arrested;
            core.ChargesCount = charges;
            core.ArrestedAssetsSeized = assetsSeized;
            core.ArrestedWalletAfter = 0;
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly and never null.
            _ = ModalCoordinator.Instance.TryShow("Arrested", new ArrestedModalPayloadDto
            {
                ChargesCount = charges,
                AssetsSeizedSnapshot = assetsSeized,
                WalletBalanceAfter = 0,
                LastChoiceResult = core.LastChoiceResult.ToString()
            });
#pragma warning restore CIVIC098
            ResetPoliceState(ref police);
            PublishPoliceInvestigationEnded();
            m_ReputationService.OnCaught();

            m_EventBus?.SafePublish(new CorruptionNarrativeEvent(
                CorruptionNarrativeEventType.Arrest,
                ChargesCount: charges,
                StolenAmount: assetsSeized));

            int gameDay = m_GetGameDay();
            m_EventBus?.SafePublish(new GameOverEvent(DefeatCause.Arrested, gameDay), "CountermeasuresUpdateSystem");
            Log.Info($"DEFEAT: Arrested on day {gameDay}");
            CmWalletOps.UnfreezeViaEcb(ecb);
            CmWalletOps.ConfiscateViaEcb(ecb);
        }

        private void PublishInvestigationStopped(string journalistName, string reason, string? bribeAmount = null)
        {
            m_EventBus?.SafePublish(new CorruptionNarrativeEvent(CorruptionNarrativeEventType.InvestigationStopped));
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame
            var data = new Dictionary<string, string>
#pragma warning restore CIVIC050
            {
                ["JournalistName"] = journalistName,
                ["Reason"] = reason
            };
            if (bribeAmount != null)
                data["BribeAmount"] = bribeAmount;

            m_EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.SatireInvestStop.ToKey(), data));
        }

        private void PublishPoliceInvestigationEnded()
        {
            m_EventBus?.SafePublish(new CorruptionNarrativeEvent(CorruptionNarrativeEventType.PoliceInvestigationEnded));
        }

        // ============================================================================
        // RESET HELPERS
        // ============================================================================

        public static void ResetInvestigationState(ref CmInvestigationState inv)
        {
            inv.Active = false;
            inv.Progress = 0;
            inv.LastMilestone = 0;
            inv.StartHour = 0f;
            inv.Journalist = default;
            inv.BribeCost = 0;
            inv.WaitingForChoice = false;
        }

        public static void ResetPoliceState(ref CmPoliceState police)
        {
            police.Active = false;
            police.StartHour = 0f;
            police.ChargesCount = 0;
            police.WaitingForChoice = false;
        }
    }
}
