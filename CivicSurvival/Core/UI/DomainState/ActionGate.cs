using System;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.UI.DomainState
{
    public enum ActionKey
    {
        CitySchedule = 0,
        DistrictSchedule = 1,
        BackupModernization = 2,
        ShadowTrade = 3,
        BuckwheatProcurement = 4,
        CivilianRepair = 5,
        PlantRepair = 6,
        ShadyContractAccept = 7,
        EmergencyFundPreset = 8,
        FuelSiphonPreset = 9,
        ShadowImport = 10,
        ShadowExport = 11,
        InvestigationBribe = 12,
        PoliceBribe = 13
    }

    public readonly struct ActionContext
    {
        public readonly bool HasWaveState;
        public readonly GamePhase CurrentPhase;
        public readonly bool HasScenarioState;
        public readonly Act CurrentAct;
        public readonly bool HasShadowWalletState;
        public readonly bool ShadowWalletFrozen;
        public readonly long ShadowWalletBalance;
        public readonly float ShadowWalletSanctionsMarkup;
        public readonly long ProposedCost;

        public ActionContext(
            bool hasWaveState,
            GamePhase currentPhase,
            bool hasScenarioState,
            Act currentAct)
            : this(
                hasWaveState,
                currentPhase,
                hasScenarioState,
                currentAct,
                false,
                true,
                0,
                0f,
                0)
        {
        }

        public ActionContext(
            bool hasWaveState,
            GamePhase currentPhase,
            bool hasScenarioState,
            Act currentAct,
            bool hasShadowWalletState,
            bool shadowWalletFrozen,
            long shadowWalletBalance,
            float shadowWalletSanctionsMarkup,
            long proposedCost)
        {
            HasWaveState = hasWaveState;
            CurrentPhase = currentPhase;
            HasScenarioState = hasScenarioState;
            CurrentAct = currentAct;
            HasShadowWalletState = hasShadowWalletState;
            ShadowWalletFrozen = shadowWalletFrozen;
            ShadowWalletBalance = shadowWalletBalance;
            ShadowWalletSanctionsMarkup = shadowWalletSanctionsMarkup;
            ProposedCost = proposedCost;
        }

        public ActionContext WithWallet(in ShadowWalletSingleton wallet) =>
            new(
                HasWaveState,
                CurrentPhase,
                HasScenarioState,
                CurrentAct,
                true,
                wallet.IsFrozen,
                wallet.Balance,
                wallet.SanctionsMarkup,
                ProposedCost);

        public ActionContext WithWalletState(bool isFrozen, long balance, float sanctionsMarkup) =>
            new(
                HasWaveState,
                CurrentPhase,
                HasScenarioState,
                CurrentAct,
                true,
                isFrozen,
                balance,
                sanctionsMarkup,
                ProposedCost);

        public ActionContext WithCost(long proposedCost) =>
            new(
                HasWaveState,
                CurrentPhase,
                HasScenarioState,
                CurrentAct,
                HasShadowWalletState,
                ShadowWalletFrozen,
                ShadowWalletBalance,
                ShadowWalletSanctionsMarkup,
                proposedCost);
    }

    public readonly struct ActionAvailabilityField
    {
        public readonly bool CanRun;
        public readonly string LockedReasonId;
        public readonly long EffectiveCost;

        private ActionAvailabilityField(bool canRun, string lockedReasonId, long effectiveCost)
        {
            CanRun = canRun;
            LockedReasonId = lockedReasonId ?? string.Empty;
            EffectiveCost = effectiveCost;
        }

        internal static ActionAvailabilityField Allow(long effectiveCost = 0) => new(true, string.Empty, effectiveCost);
        internal static ActionAvailabilityField Reject(string reasonId, long effectiveCost = 0) => new(false, reasonId, effectiveCost);

        public ActionAvailabilityField And(ActionAvailabilityField next) =>
            CanRun ? next : this;

        public ActionAvailabilityField WithEffectiveCost(long effectiveCost) =>
            new(CanRun, LockedReasonId, effectiveCost);
    }

    public static class ActionGate
    {
        public static ActionAvailabilityField Resolve(ActionKey key, in ActionContext ctx)
        {
            return key switch
            {
                // Schedule/blackout switches are how the player saves electricity
                // during attacks (cut power to non-critical districts so hospitals and
                // HQ stay online). Blocking them during a wave defeats the mechanic.
                // RepairBlockedDuringWave stays applied to actual repair (Civilian /
                // Plant) below — the name now matches the use site.
                ActionKey.CitySchedule => ActionAvailabilityField.Allow(),
                ActionKey.DistrictSchedule => ActionAvailabilityField.Allow(),
                ActionKey.BackupModernization => RequireAct(Act.Crisis, ctx),
                ActionKey.ShadowTrade => RequireAct(Act.Crisis, ctx),
                ActionKey.BuckwheatProcurement => RequireAct(Act.Crisis, ctx),
                ActionKey.CivilianRepair => RequirePhaseSafe(ctx),
                ActionKey.PlantRepair => RequirePhaseSafe(ctx),
                ActionKey.ShadyContractAccept => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireWalletNotFrozen(ctx))
                    .And(RequireAffordable(ctx)),
                ActionKey.EmergencyFundPreset => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireWalletNotFrozen(ctx)),
                ActionKey.FuelSiphonPreset => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireWalletNotFrozen(ctx)),
                ActionKey.ShadowImport => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireShadowImportSpendAllowed(ctx)),
                ActionKey.ShadowExport => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireWalletNotFrozen(ctx)),
                ActionKey.InvestigationBribe => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireWalletNotFrozen(ctx))
                    .And(RequireAffordable(ctx)),
                ActionKey.PoliceBribe => RequireAct(Act.Crisis, ctx)
                    .And(RequireWalletAvailable(ctx))
                    .And(RequireAffordableBypassFreeze(ctx)),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown action key")
            };
        }

        private static ActionAvailabilityField RequirePhaseSafe(in ActionContext ctx)
        {
            if (!ctx.HasWaveState)
                return ActionAvailabilityField.Allow();

            var wave = new WaveStateSingleton { CurrentPhase = ctx.CurrentPhase };
            return wave.TryRequirePhaseSafe(out var reasonId)
                ? ActionAvailabilityField.Allow()
                : ActionAvailabilityField.Reject(reasonId);
        }

        private static ActionAvailabilityField RequireAct(Act required, in ActionContext ctx)
        {
            if (!ctx.HasScenarioState)
                return ActionAvailabilityField.Allow();

            var actSingleton = new CurrentActSingleton { CurrentAct = ctx.CurrentAct };
            return actSingleton.TryRequireAct(required, out var reasonId)
                ? ActionAvailabilityField.Allow()
                : ActionAvailabilityField.Reject(reasonId);
        }

        private static ActionAvailabilityField RequireWalletAvailable(in ActionContext ctx)
        {
            return ctx.HasShadowWalletState
                ? ActionAvailabilityField.Allow()
                : ActionAvailabilityField.Reject(ReasonIds.MarketWalletUnavailable);
        }

        private static ActionAvailabilityField RequireWalletNotFrozen(in ActionContext ctx)
        {
            return ctx.ShadowWalletFrozen
                ? ActionAvailabilityField.Reject(ReasonIds.MarketFreezeFrozen)
                : ActionAvailabilityField.Allow();
        }

        private static ActionAvailabilityField RequireAffordable(in ActionContext ctx)
        {
            long effectiveCost = EffectiveCost(ctx);
            if (effectiveCost <= 0)
                return ActionAvailabilityField.Allow(effectiveCost);

            long pending = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).PendingDeductions;
            return ctx.ShadowWalletBalance - pending >= effectiveCost
                ? ActionAvailabilityField.Allow(effectiveCost)
                : ActionAvailabilityField.Reject(ReasonIds.MarketInsufficientFunds, effectiveCost);
        }

        // Intentionally named distinct from RequireAffordable to encode "bypass freeze" semantics at call site.
        // Body delegates to avoid S4144 duplication.
        private static ActionAvailabilityField RequireAffordableBypassFreeze(in ActionContext ctx) =>
            RequireAffordable(ctx);

        private static ActionAvailabilityField RequireShadowImportSpendAllowed(in ActionContext ctx)
        {
            if (ctx.ProposedCost <= 0)
                return ActionAvailabilityField.Allow();

            return RequireWalletNotFrozen(ctx).And(RequireAffordable(ctx));
        }

        private static long EffectiveCost(in ActionContext ctx)
        {
            return ctx.ProposedCost <= 0
                ? 0
                : SanctionsCostHelper.ApplyMarkup(ctx.ProposedCost, ctx.ShadowWalletSanctionsMarkup);
        }
    }
}
