using System;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Intel;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.UI.DomainState
{
    public static class WaveStateEligibility
    {
        public static bool TryRequirePhaseSafe(this WaveStateSingleton waveState, out string reasonId)
        {
            if (waveState.CurrentPhase == GamePhase.Attack || waveState.CurrentPhase == GamePhase.Alert)
            {
                reasonId = ReasonIds.RepairBlockedDuringWave;
                return false;
            }

            reasonId = "";
            return true;
        }
    }

    public static class ScenarioStateEligibility
    {
        public static bool TryRequireWar(this ScenarioSingleton scenario, out string reasonId)
        {
            if (!scenario.IsWarStarted)
            {
                reasonId = ReasonIds.PreWarLocked;
                return false;
            }

            reasonId = string.Empty;
            return true;
        }

        public static bool TryRequireAct(this CurrentActSingleton actSingleton, Act required, out string reasonId)
        {
            if (actSingleton.CurrentAct < required)
            {
                reasonId = ReasonIds.ActLockedFor(required);
                return false;
            }

            reasonId = string.Empty;
            return true;
        }

        public static bool TryRequireVictory(this ScenarioSingleton scenario, string activeModalId, out string reasonId)
        {
            if (activeModalId != "Victory")
            {
                reasonId = ReasonIds.VictoryNotActive;
                return false;
            }

            reasonId = string.Empty;
            return true;
        }
    }

    public static class DonorEligibility
    {
        public static bool CanDonateFunds(bool matrixAvailable, TrustLevel trust, out string reasonId)
        {
            if (matrixAvailable)
            {
                reasonId = "";
                return true;
            }

            reasonId = trust == TrustLevel.Refused
                ? ReasonIds.DonorTrustRefused
                : ReasonIds.DonorFundsUnavailable;
            return false;
        }

        public static bool CanProvidePower(
            bool matrixAvailable,
            bool hasAvailableGenerators,
            TrustLevel trust,
            AidTier shockTier,
            out string reasonId)
        {
            if (matrixAvailable && hasAvailableGenerators)
            {
                reasonId = "";
                return true;
            }

            if (trust > TrustLevel.Partial)
            {
                reasonId = ReasonIds.DonorTrustInsufficient;
                return false;
            }
            if (shockTier < AidTier.Headlines)
            {
                reasonId = ReasonIds.DonorShockInsufficient;
                return false;
            }
            reasonId = !hasAvailableGenerators
                ? ReasonIds.DonorGeneratorCap
                : ReasonIds.DonorPowerUnavailable;
            return false;
        }

        public static bool CanProvideDefense(
            bool matrixAvailable,
            bool airDefenseAvailable,
            bool donorPatriotCreditCapReached,
            TrustLevel trust,
            AidTier shockTier,
            out string reasonId)
        {
            if (matrixAvailable && airDefenseAvailable && !donorPatriotCreditCapReached)
            {
                reasonId = "";
                return true;
            }

            if (trust != TrustLevel.Full)
            {
                reasonId = ReasonIds.DonorTrustInsufficient;
                return false;
            }
            if (shockTier != AidTier.GlobalShock)
            {
                reasonId = ReasonIds.DonorShockInsufficient;
                return false;
            }
            if (!matrixAvailable)
            {
                reasonId = ReasonIds.DonorConferenceUnavailable;
                return false;
            }
            if (!airDefenseAvailable)
            {
                reasonId = ReasonIds.DonorDefenseUnavailable;
                return false;
            }
            reasonId = donorPatriotCreditCapReached
                ? ReasonIds.DonorPatriotCap
                : ReasonIds.DonorDefenseUnavailable;
            return false;
        }
    }

    public static class BackupPowerEligibility
    {
        public static bool CanSetBackupPolicy(Act currentAct, out string reasonId)
        {
            if (currentAct < Act.Crisis)
            {
                reasonId = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }

            reasonId = string.Empty;
            return true;
        }
    }

    public static class CityScheduleEligibility
    {
        public static bool CanSetCitySchedule(GamePhase currentPhase, out string reasonId)
        {
            if (currentPhase == GamePhase.Attack || currentPhase == GamePhase.Alert)
            {
                reasonId = ReasonIds.RepairBlockedDuringWave;
                return false;
            }

            reasonId = string.Empty;
            return true;
        }
    }

    public static class AirDefenseEligibility
    {
        // Per-AAType resupply gates. Each named entry point exists because DtoEligibility binds a
        // DTO field to a predicate by name (nameof), so each per-type button needs its own method.
        // All four delegate to the shared CanEmergencyResupply logic with that type's own
        // live-installation / deficit / cost / cooldown aggregates — one rule, four bindings, no copies.
        // onCooldownOfType gates the dear types (Patriot) between refills; cheap types pass false.
        public static bool CanResupplyPatriot(bool hasLiveOfType, bool hasDeficitOfType, bool onCooldownOfType, long cost, World world, out string reasonId)
            => CanEmergencyResupply(hasLiveOfType, hasDeficitOfType, onCooldownOfType, cost, world, out reasonId);

        // The three gun types share one "restock guns" button, so they share one gate: live =
        // any gun present, deficit = any gun under-supplied, cost = summed gun resupply cost.
        // Guns carry no cooldown, so the cooldown arm is fixed false.
        public static bool CanResupplyGuns(bool hasLiveGuns, bool hasGunDeficit, long cost, World world, out string reasonId)
            => CanEmergencyResupply(hasLiveGuns, hasGunDeficit, onCooldown: false, cost, world, out reasonId);

        public static bool CanEmergencyResupply(
            bool hasLiveInstallations,
            bool hasAmmoDeficit,
            bool onCooldown,
            long cost,
            World world,
            out string reasonId)
        {
            if (!hasLiveInstallations)
            {
                reasonId = ReasonIds.AaNoLiveInstallations;
                return false;
            }

            if (!hasAmmoDeficit)
            {
                reasonId = ReasonIds.AaAmmoFull;
                return false;
            }

            if (onCooldown)
            {
                reasonId = ReasonIds.AaResupplyCooldown;
                return false;
            }

            if (cost <= 0)
            {
                reasonId = ReasonIds.AaConfigError;
                return false;
            }

            if (!CityBudgetService.CanAffordWithPending(world, cost))
            {
                reasonId = ReasonIds.AaInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        /// <summary>
        /// Remaining resupply cooldown in game-hours for one AA type, shared by the UI gate and the
        /// backend guard so both agree. 0 = not on cooldown (cooldown disabled, never resupplied, or
        /// elapsed). <paramref name="lastResupplyHour"/> uses the AirDefenseCreditsSingleton.NoResupplyHour
        /// sentinel (&lt; 0) for "never resupplied".
        /// </summary>
        public static float ResupplyCooldownRemainingHours(float currentGameHour, float lastResupplyHour, float cooldownHours)
        {
            if (cooldownHours <= 0f || lastResupplyHour < 0f)
                return 0f;

            float elapsed = currentGameHour - lastResupplyHour;
            float remaining = cooldownHours - elapsed;
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// True while the per-wave resupply cooldown is still active for an AA type (Patriot — one
        /// resupply per wave). Shared by the UI gate and the backend guard so both agree.
        ///
        /// Contract: <paramref name="currentWave"/> and <paramref name="lastResupplyWave"/> are real
        /// wave numbers (&gt;= 1) or the AirDefenseCreditsSingleton.NoResupplyWave sentinel (&lt; 0)
        /// for "never resupplied" — the producer (AirDefenseStateSystem.RecordResupply) never records
        /// a non-positive wave, so the field never holds 0. A <paramref name="currentWave"/> &lt;= 0
        /// means NO active wave (initial Calm before wave 1, or WaveStateSingleton momentarily absent
        /// → fallback 0): there is no "current wave" to be limited within, so the per-wave cooldown
        /// does not apply. This early-out also keeps a stale fallback currentWave from reading as a
        /// negative <c>currentWave - lastResupplyWave</c> delta (which would falsely block).
        /// <paramref name="cooldownWaves"/> 0 = disabled (the gun types). Otherwise blocked while fewer
        /// than <paramref name="cooldownWaves"/> waves have elapsed since the last resupply — with the
        /// default 1, that is the same wave number.
        /// </summary>
        public static bool IsResupplyWaveCooldownActive(int currentWave, int lastResupplyWave, int cooldownWaves)
        {
            if (cooldownWaves <= 0 || lastResupplyWave < 0 || currentWave <= 0)
                return false;

            return currentWave - lastResupplyWave < cooldownWaves;
        }

        public static bool CanPlaceHeritageBofors(int credits, int availableManpower, int crewRequired, out string reasonId)
            => CanPlaceCreditAA(credits, availableManpower, crewRequired, out reasonId);

        public static bool CanPlaceDonorPatriot(int credits, int availableManpower, int crewRequired, out string reasonId)
            => CanPlaceCreditAA(credits, availableManpower, crewRequired, out reasonId);

        // Paid AA placement gate, shared by Bofors and Patriot. The two named wrappers
        // below exist because DtoEligibility binds a field to a predicate by name (nameof),
        // so each paid card needs its own named entry point into the shared logic.
        public static bool CanPlacePaidBofors(int price, int availableManpower, int crewRequired, World world, out string reasonId)
            => CanPlacePaidAA(price, availableManpower, crewRequired, world, out reasonId);

        public static bool CanPlacePaidGepard(int price, int availableManpower, int crewRequired, World world, out string reasonId)
            => CanPlacePaidAA(price, availableManpower, crewRequired, world, out reasonId);

        public static bool CanPlacePaidPatriot(int price, int availableManpower, int crewRequired, World world, out string reasonId)
            => CanPlacePaidAA(price, availableManpower, crewRequired, world, out reasonId);

        public static bool CanPlacePaidAA(int price, int availableManpower, int crewRequired, World world, out string reasonId)
        {
            if (price <= 0)
            {
                reasonId = ReasonIds.AaConfigError;
                return false;
            }

            if (!CanMeetAACrew(availableManpower, crewRequired, out reasonId))
                return false;

            if (!CityBudgetService.CanAffordWithPending(world, price))
            {
                reasonId = ReasonIds.AaInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        /// <summary>
        /// How many paid AA installations of one kind the player can field right now — the
        /// smaller of the budget limit (available funds / price) and the manpower limit (free
        /// manpower / crew). Mirrors the two gates in <see cref="CanPlacePaidAA"/> so the count
        /// and the place button never disagree. Shared by Bofors and Patriot (different price/crew).
        /// </summary>
        public static int AffordablePaidAACount(int price, int availableManpower, int crewRequired, World world)
        {
            if (price <= 0 || crewRequired <= 0)
                return 0;

            int manpowerLimit = availableManpower / crewRequired;
            long availableFunds = CityBudgetService.GetBalance(world) - CityBudgetService.PendingDeductions;
            long budgetLimit = availableFunds > 0 ? availableFunds / price : 0;
            // manpowerLimit (int) upper-bounds the result; clamp to int range explicitly for the analyzer.
            long affordable = System.Math.Max(0L, System.Math.Min(manpowerLimit, budgetLimit));
            return affordable > int.MaxValue ? int.MaxValue : (int)affordable;
        }

        public static bool CanPayAirDefenseBudget(long cost, World world, out string reasonId)
        {
            if (cost <= 0)
            {
                reasonId = "";
                return true;
            }

            if (!CityBudgetService.CanAffordWithPending(world, cost))
            {
                reasonId = ReasonIds.AaInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        private static bool CanPlaceCreditAA(int credits, int availableManpower, int crewRequired, out string reasonId)
        {
            if (credits <= 0)
            {
                reasonId = ReasonIds.AaNoCredit;
                return false;
            }

            return CanMeetAACrew(availableManpower, crewRequired, out reasonId);
        }

        private static bool CanMeetAACrew(int availableManpower, int crewRequired, out string reasonId)
        {
            if (availableManpower < crewRequired)
            {
                reasonId = ReasonIds.AaInsufficientManpower;
                return false;
            }

            reasonId = "";
            return true;
        }
    }

    public static class BuckwheatEligibility
    {
        private const int ProcurementLevel25 = 25;
        private const int ProcurementLevel50 = 50;
        private const int ProcurementLevel75 = 75;
        private const int ProcurementLevel100 = 100;

        public static bool CanDistribute(bool canDistribute, out string reasonId)
        {
            if (!canDistribute)
            {
                reasonId = ReasonIds.ReliefNotEnoughReserve;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanAffordProcurement(int procurementLevel, World world, out string reasonId)
            => CanAffordProcurement(procurementLevel, intervalsDue: BuckwheatProcurementAffordabilityIntervals, world, out reasonId, out _);

        public static bool CanAffordProcurement(
            int procurementLevel,
            int intervalsDue,
            World world,
            out string reasonId,
            out int baseCost)
        {
            baseCost = CalculateProcurementBaseCost(procurementLevel, intervalsDue);
            if (procurementLevel <= 0)
            {
                reasonId = "";
                return true;
            }

            if (baseCost <= 0)
            {
                reasonId = ReasonIds.ReliefProcurementConfigError;
                return false;
            }

            if (!ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).CanAffordWithPending(baseCost).Affordable)
            {
                reasonId = ReasonIds.ReliefProcurementInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanSetProcurement25(int currentLevel, World world, out string reasonId)
            => CanSelectProcurementLevel(ProcurementLevel25, currentLevel, world, out reasonId);

        public static bool CanSetProcurement50(int currentLevel, World world, out string reasonId)
            => CanSelectProcurementLevel(ProcurementLevel50, currentLevel, world, out reasonId);

        public static bool CanSetProcurement75(int currentLevel, World world, out string reasonId)
            => CanSelectProcurementLevel(ProcurementLevel75, currentLevel, world, out reasonId);

        public static bool CanSetProcurement100(int currentLevel, World world, out string reasonId)
            => CanSelectProcurementLevel(ProcurementLevel100, currentLevel, world, out reasonId);

        public static bool CanSelectProcurementLevel(int targetLevel, int currentLevel, World world, out string reasonId)
        {
            if (targetLevel <= 0 || targetLevel <= currentLevel)
            {
                reasonId = "";
                return true;
            }

            return CanAffordProcurement(targetLevel, world, out reasonId);
        }

        private const int BuckwheatProcurementAffordabilityIntervals = 2;

        public static int CalculateProcurementBaseCost(int procurementLevel, int intervalsDue = 1)
        {
            if (procurementLevel <= 0 || intervalsDue <= 0)
                return 0;

            var haCfg = BalanceConfig.Current.HumanitarianAid;
            float interval = Math.Max(haCfg.ProcurementIntervalHours, 0.1f);
            float intervalsPerDay = Math.Max(CivicSurvival.Core.Utils.GameRate.HOURS_PER_DAY / interval, 1f);
            float tonsThisInterval = haCfg.TonsPerDayAt100 *
                                     procurementLevel / 100f /
                                     intervalsPerDay *
                                     intervalsDue;
            return (int)Math.Round(tonsThisInterval * haCfg.CostPerTon);
        }
    }

    public static class CognitiveDistrictEligibility
    {
        public static bool CanInternetReach(GlobalInternetMode internetMode, int districtIndex, bool districtDisabled)
        {
            if (internetMode == GlobalInternetMode.Blackout)
                return false;

            if (internetMode != GlobalInternetMode.Open)
                return true;

            return districtIndex == 0 || !districtDisabled;
        }
    }

    public static class HeroEligibility
    {
        public static bool CanDeployHero(
            Act currentAct,
            bool hasPendingHeroDeployBudget,
            HeroStatus requestedMode,
            HeroStatus currentStatus,
            int deployCost,
            World world,
            out string reasonId)
        {
            if (currentAct == Act.PreWar)
            {
                reasonId = ReasonIds.HeroPrewarLocked;
                return false;
            }

            if (hasPendingHeroDeployBudget)
            {
                reasonId = ReasonIds.HeroBudgetPending;
                return false;
            }

            if (requestedMode == HeroStatus.Inactive)
            {
                reasonId = ReasonIds.HeroInvalidMode;
                return false;
            }

            if (currentStatus != HeroStatus.Inactive)
            {
                reasonId = ReasonIds.HeroRecallFirst;
                return false;
            }

            if (deployCost <= 0)
            {
                reasonId = ReasonIds.HeroConfigError;
                return false;
            }

            if (!CityBudgetService.CanAffordWithPending(world, deployCost))
            {
                reasonId = ReasonIds.HeroInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanDeployHero(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        public static bool CanRecallHero(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        public static bool CanSetHeroCounter(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        public static bool CanSetHeroLecturing(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        private static bool FromBackend(bool backendVerdict, string backendReasonId, out string reasonId)
        {
            reasonId = backendVerdict ? "" : backendReasonId ?? "";
            return backendVerdict;
        }
    }

    public static class IntelEligibility
    {
        public static bool CanBuyInsider(
            bool hasInsider,
            long baseCost,
            World world,
            out string reasonId,
            out long effectiveCost)
        {
            effectiveCost = 0;
            if (hasInsider)
            {
                reasonId = ReasonIds.InsiderAlreadyActive;
                return false;
            }

            if (baseCost <= 0)
            {
                reasonId = ReasonIds.InsiderConfigError;
                return false;
            }

            var insiderAff = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).CanAffordWithPending(baseCost);
            effectiveCost = insiderAff.EffectiveCost;
            if (!insiderAff.Affordable)
            {
                reasonId = ReasonIds.InsiderInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanUpgradeIntel(
            bool isMaxIntelUpgrade,
            long baseCost,
            World world,
            out string reasonId,
            out long effectiveCost)
        {
            effectiveCost = 0;
            if (isMaxIntelUpgrade)
            {
                reasonId = ReasonIds.GwIntelMax;
                return false;
            }

            if (baseCost <= 0)
            {
                reasonId = ReasonIds.GwIntelConfigError;
                return false;
            }

            var intelAff = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).CanAffordWithPending(baseCost);
            effectiveCost = intelAff.EffectiveCost;
            if (!intelAff.Affordable)
            {
                reasonId = ReasonIds.GwInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }
    }

    public static class GridOperationEligibility
    {
        public static bool CanPrepareDrone(Act currentAct, bool walletAvailable, bool walletFrozen, long availableBalance, long cost, bool duplicate, bool hasEmptySlot, out string reasonId)
            => CanPrepareOperation(currentAct, walletAvailable, walletFrozen, availableBalance, cost, duplicate, hasEmptySlot, out reasonId);

        public static bool CanPrepareBlackout(Act currentAct, bool walletAvailable, bool walletFrozen, long availableBalance, long cost, bool duplicate, bool hasEmptySlot, out string reasonId)
            => CanPrepareOperation(currentAct, walletAvailable, walletFrozen, availableBalance, cost, duplicate, hasEmptySlot, out reasonId);

        public static bool CanPrepareDisinfo(Act currentAct, bool walletAvailable, bool walletFrozen, long availableBalance, long cost, bool duplicate, bool hasEmptySlot, out string reasonId)
            => CanPrepareOperation(currentAct, walletAvailable, walletFrozen, availableBalance, cost, duplicate, hasEmptySlot, out reasonId);

        public static bool CanPrepareOperation(
            Act currentAct,
            bool walletAvailable,
            bool walletFrozen,
            long availableBalance,
            long cost,
            bool duplicate,
            bool hasEmptySlot,
            out string reasonId)
        {
            if (currentAct < Act.Adaptation)
            {
                reasonId = ReasonIds.GwLockedReason;
                return false;
            }

            if (!walletAvailable)
            {
                reasonId = ReasonIds.GwWalletUnavailable;
                return false;
            }

            if (duplicate)
            {
                reasonId = ReasonIds.GwDuplicateOperation;
                return false;
            }

            if (!hasEmptySlot)
            {
                reasonId = ReasonIds.GwNoEmptySlot;
                return false;
            }

            if (walletFrozen || availableBalance < cost)
            {
                reasonId = ReasonIds.GwInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }
    }

    public readonly struct EligibilityFlag
    {
        public readonly bool CanRun;
        public readonly string LockedReasonId;

        private EligibilityFlag(bool canRun, string lockedReasonId)
        {
            CanRun = canRun;
            LockedReasonId = lockedReasonId ?? "";
        }

        internal static EligibilityFlag Allow() => new(true, "");
        internal static EligibilityFlag Reject(string reasonId) => new(false, reasonId);
    }

    public static class PlantRepairEligibility
    {
        public static EligibilityFlag ForPlantRepair(
            GamePhase currentPhase,
            bool hasPendingRepair,
            bool foundPlant,
            bool canApplyRepairState,
            bool isUnderRepair,
            int billableRepairPercent,
            RepairType repairType,
            World world)
        {
            if (currentPhase == GamePhase.Attack || currentPhase == GamePhase.Alert || currentPhase == GamePhase.Recovery)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairWaveActive);
            }

            if (hasPendingRepair)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairPending);
            }

            if (!foundPlant)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairNotFound);
            }

            if (!canApplyRepairState)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairSystemNotReady);
            }

            if (isUnderRepair)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairAlreadyRepairing);
            }

            if (billableRepairPercent <= 0)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairNoDamage);
            }

            var repairParams = RepairPaymentHelper.CalculateRepairParams(billableRepairPercent, repairType);
            if (repairParams.Cost <= 0)
            {
                return EligibilityFlag.Reject(ReasonIds.PlantRepairConfigError);
            }

            bool canAfford = repairType == RepairType.ShadowOps
                ? ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).CanAffordWithPending(repairParams.Cost).Affordable
                : CityBudgetService.CanAffordWithPending(world, repairParams.Cost);
            if (!canAfford)
            {
                return EligibilityFlag.Reject(repairType == RepairType.ShadowOps
                    ? ReasonIds.PlantsShadowInsufficientFunds
                    : ReasonIds.InsufficientFunds);
            }

            return EligibilityFlag.Allow();
        }

        public static bool CanRepairPlant(
            GamePhase currentPhase,
            bool hasPendingRepair,
            bool foundPlant,
            bool canApplyRepairState,
            bool isUnderRepair,
            int billableRepairPercent,
            RepairType repairType,
            World world,
            out string reasonId)
        {
            var eligibility = ForPlantRepair(
                currentPhase,
                hasPendingRepair,
                foundPlant,
                canApplyRepairState,
                isUnderRepair,
                billableRepairPercent,
                repairType,
                world);
            reasonId = eligibility.LockedReasonId;
            return eligibility.CanRun;
        }
    }

    public static class CivilianRepairEligibility
    {
        public static EligibilityFlag ForCivilianRepair(
            bool waveBlocked,
            string waveReasonId,
            int hitCount,
            RepairType repairType,
            World world)
        {
            if (waveBlocked)
                return EligibilityFlag.Reject(waveReasonId);

            if (hitCount <= 0)
                return EligibilityFlag.Reject(ReasonIds.CivilianRepairNotFound);

            var repairParams = RepairPaymentHelper.CalculateCivilianRepairParams(hitCount, repairType);
            if (repairParams.Cost <= 0)
                return EligibilityFlag.Reject(ReasonIds.PlantRepairConfigError);

            bool canAfford = repairType == RepairType.ShadowOps
                ? ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance).CanAffordWithPending(repairParams.Cost).Affordable
                : CityBudgetService.CanAffordWithPending(world, repairParams.Cost);
            if (!canAfford)
            {
                return EligibilityFlag.Reject(repairType == RepairType.ShadowOps
                    ? ReasonIds.InfraCivilianShadowInsufficient
                    : ReasonIds.InsufficientFunds);
            }

            return EligibilityFlag.Allow();
        }
    }

    public static class ModernizationEligibility
    {
        public static EligibilityFlag ForModernization(
            bool hasPendingProcurement,
            int daysUntilNextProcurement,
            ContractorType contractor,
            int buildingCount,
            long totalCost,
            World world)
        {
            if (hasPendingProcurement)
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationPending);
            }

            if (daysUntilNextProcurement > 0)
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationCooldown);
            }

            if (contractor == ContractorType.None)
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationInvalidContractor);
            }

            if (buildingCount == 0)
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationNoTargets);
            }

            if (totalCost <= 0)
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationConfigError);
            }

            if (!CityBudgetService.CanAffordWithPending(world, totalCost))
            {
                return EligibilityFlag.Reject(ReasonIds.BackupModernizationInsufficientFunds);
            }

            return EligibilityFlag.Allow();
        }

        public static bool CanModernizeDistrict(
            bool hasPendingProcurement,
            int daysUntilNextProcurement,
            ContractorType contractor,
            int buildingCount,
            long totalCost,
            World world,
            out string reasonId)
        {
            var eligibility = ForModernization(
                hasPendingProcurement,
                daysUntilNextProcurement,
                contractor,
                buildingCount,
                totalCost,
                world);
            reasonId = eligibility.LockedReasonId;
            return eligibility.CanRun;
        }
    }

    public static class ShadowImportEligibility
    {
        public static bool CanSetImportMW(
            bool importStateAvailable,
            bool walletAvailable,
            bool walletFrozen,
            long walletBalance,
            long effectiveCost,
            out string reasonId)
        {
            if (!importStateAvailable)
            {
                reasonId = ReasonIds.MarketStateUnavailable;
                return false;
            }

            if (!walletAvailable)
            {
                reasonId = ReasonIds.MarketWalletUnavailable;
                return false;
            }

            if (effectiveCost <= 0)
            {
                reasonId = "";
                return true;
            }

            if (walletFrozen)
            {
                reasonId = ReasonIds.MarketFreezeFrozen;
                return false;
            }

            if (walletBalance < effectiveCost)
            {
                reasonId = ReasonIds.MarketInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool ShadowImportAvailable(bool isSanctioned, bool walletFrozen, out string reasonId)
        {
            if (isSanctioned)
            {
                reasonId = ReasonIds.MarketSanctioned;
                return false;
            }

            if (walletFrozen)
            {
                reasonId = ReasonIds.MarketFreezeFrozen;
                return false;
            }

            reasonId = "";
            return true;
        }
    }

    public static class SchemesEligibility
    {
        public static bool CanSetEmergencyFund(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        public static bool CanSetFuelSiphon(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        private static bool FromBackend(bool backendVerdict, string backendReasonId, out string reasonId)
        {
            reasonId = backendVerdict ? "" : backendReasonId ?? "";
            return backendVerdict;
        }
    }

    public static class MobilizationEligibility
    {
        public static bool CanCallToArms(bool inCrisis, int casualties, bool isCooldown, out string reasonId)
        {
            if (!inCrisis)
            {
                reasonId = ReasonIds.MobNotInCrisis;
                return false;
            }

            if (casualties <= 0)
            {
                reasonId = ReasonIds.MobNoCasualties;
                return false;
            }

            if (isCooldown)
            {
                reasonId = ReasonIds.MobCallToArmsCooldown;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanCallToArms(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        public static bool CanToggleConscription(bool backendVerdict, out string reasonId, string backendReasonId)
            => FromBackend(backendVerdict, backendReasonId, out reasonId);

        /// <summary>
        /// Declarative conscription toggle gate. Re-activation is blocked while the
        /// reactivation cooldown is running; deactivation is always allowed (cooldown
        /// only bites the re-enable, so the player can still drop conscription — and
        /// its happiness penalty — at any time).
        /// </summary>
        public static bool CanToggleConscription(bool reactivating, bool onCooldown, out string reasonId)
        {
            if (reactivating && onCooldown)
            {
                reasonId = ReasonIds.MobConscriptionCooldown;
                return false;
            }

            reasonId = "";
            return true;
        }

        private static bool FromBackend(bool backendVerdict, string backendReasonId, out string reasonId)
        {
            reasonId = backendVerdict ? "" : backendReasonId ?? "";
            return backendVerdict;
        }
    }

    public static class SettingsEligibility
    {
        public static bool CanToggleTelemetry(bool backendVerdict, out string reasonId, string backendReasonId)
        {
            reasonId = backendVerdict ? "" : backendReasonId ?? "";
            return backendVerdict;
        }
    }

    public static class SpotterEligibility
    {
        /// <summary>
        /// Declarative gate for SBU visits. Same-tick reservations are still owned by the command handler
        /// because DTO writers cannot see batch-local target reservations.
        /// </summary>
        public static bool CanPerformSBUVisit(
            int spotterCount,
            int activeSpotterCount,
            int sbuCost,
            World world,
            out string reasonId)
        {
            if (spotterCount <= 0) { reasonId = ReasonIds.SpotterNone; return false; }
            if (activeSpotterCount <= 0) { reasonId = ReasonIds.SpotterNoActiveTargets; return false; }
            if (sbuCost <= 0) { reasonId = ReasonIds.SpotterConfigError; return false; }
            if (!CityBudgetService.CanAffordWithPending(world, sbuCost))
            {
                reasonId = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanPerformEvacuation(
            int spotterCount,
            int activeSpotterCount,
            int evacuationCost,
            World world,
            out string reasonId)
        {
            if (spotterCount <= 0) { reasonId = ReasonIds.SpotterNone; return false; }
            if (activeSpotterCount <= 0) { reasonId = ReasonIds.SpotterNoActiveTargets; return false; }
            if (evacuationCost <= 0) { reasonId = ReasonIds.SpotterConfigError; return false; }
            if (!CityBudgetService.CanAffordWithPending(world, evacuationCost))
            {
                reasonId = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanToggleCounterOSINT(
            bool countermeasuresClosed,
            bool isCurrentlyActive,
            int dailyCost,
            World world,
            out string reasonId)
        {
            if (countermeasuresClosed) { reasonId = ReasonIds.CountermeasuresLocked; return false; }
            if (isCurrentlyActive) { reasonId = ""; return true; }
            if (dailyCost <= 0) { reasonId = ReasonIds.SpotterConfigError; return false; }
            if (!CityBudgetService.CanAffordWithPending(world, dailyCost))
            {
                reasonId = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            reasonId = "";
            return true;
        }

        public static bool CanToggleInternetForDistrict(
            Act currentAct,
            int districtIndex,
            int maxDistrictIndex,
            out string reasonId)
        {
            if (currentAct < Act.Crisis) { reasonId = ReasonIds.SpotterPrecrisisLocked; return false; }
            if (districtIndex < 0 || districtIndex > maxDistrictIndex)
            {
                reasonId = ReasonIds.SpotterInvalidDistrict;
                return false;
            }

            reasonId = "";
            return true;
        }
    }
}
