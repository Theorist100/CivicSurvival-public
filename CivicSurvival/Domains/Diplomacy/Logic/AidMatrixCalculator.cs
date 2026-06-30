using System;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Diplomacy.Data;

namespace CivicSurvival.Domains.Diplomacy.Logic
{
    /// <summary>
    /// Bitmask for weapon availability.
    /// </summary>
    [Flags]
    public enum WeaponAccess : byte
    {
        None = 0,
        S300Ammo = 1,
        IRIS_T = 2,
        NASAMS = 4,
        Patriot = 8,
        HIMARS = 16,

        BasicOnly = S300Ammo,
        Headlines = S300Ammo | IRIS_T | NASAMS,
        All = S300Ammo | IRIS_T | NASAMS | Patriot | HIMARS
    }

    /// <summary>
    /// Trust-level message IDs. React maps these to localized strings.
    /// </summary>
    public enum TrustMessageId : byte
    {
        None = 0,
        FullAccess = 1,
        AdvancedBlocked = 2,
        MinimalAidOnly = 3,
        AidRefused = 4
    }

    /// <summary>
    /// Reason why aid items are blocked. React maps these to localized strings.
    /// </summary>
    public enum BlockReasonId : byte
    {
        None = 0,
        RequiresFullTrust = 1,
        CorruptionTooHigh = 2,
        GovernanceRefusal = 3
    }

    /// <summary>
    /// What the world OFFERS based on shock level (before trust filter).
    /// Internal struct — never leaves the calculator.
    /// </summary>
    internal struct OfferedAidPackage
    {
        public int MaxFunds;
        public int AmmoResupplyPercent;
        public int GeneratorsAvailable;
        public int GeneratorMW;
        public int PatriotDays;
        public WeaponAccess WeaponsOffered;
        public bool GeneratorsOffered;
    }

    /// <summary>
    /// What you can actually ACCESS after trust filter is applied.
    /// Zero-alloc struct — no strings, no lists, no heap.
    /// React maps enum IDs to localized text.
    /// </summary>
    public struct FilteredAidPackage
    {
        /// <summary>The shock tier that determined this aid package</summary>
        public AidTier ShockTier;

        /// <summary>Current trust level with donors</summary>
        public TrustLevel TrustLevel;

        /// <summary>Monetary aid received (in dollars)</summary>
        public int Funds;

        /// <summary>Percentage of ammo resupply available (0-100)</summary>
        public int AmmoResupplyPercent;

        /// <summary>Number of emergency generators received</summary>
        public int Generators;

        /// <summary>Megawatts per generator</summary>
        public int GeneratorMW;

        /// <summary>Days of Patriot system availability</summary>
        public int PatriotDays;

        /// <summary>Weapons accessible at current trust level (bitmask)</summary>
        public WeaponAccess WeaponsAccessible;

        /// <summary>Whether generators are accessible at current trust level</summary>
        public bool GeneratorsAccessible;

        /// <summary>Weapons offered but blocked by trust (bitmask)</summary>
        public WeaponAccess WeaponsOfferedButBlocked;

        /// <summary>Whether economic sanctions are applied</summary>
        public bool SanctionsApplied;

        /// <summary>Trust status message ID — React maps to localized string</summary>
        public TrustMessageId TrustMessage;

        /// <summary>Block reason ID — React maps to localized string</summary>
        public BlockReasonId BlockedReason;

        /// <summary>True if any items are blocked or sanctions applied</summary>
        public readonly bool HasBlockedItems => WeaponsOfferedButBlocked != WeaponAccess.None || SanctionsApplied;

        /// <summary>True if Patriot system is accessible</summary>
        public readonly bool CanGetPatriot => (WeaponsAccessible & WeaponAccess.Patriot) != 0;

        /// <summary>True if Patriot was offered but blocked by trust</summary>
        public readonly bool PatriotOfferedButBlocked => (WeaponsOfferedButBlocked & WeaponAccess.Patriot) != 0;

        /// <summary>True if any aid (funds, generators, or weapons) is available</summary>
        public readonly bool HasAnyAid => Funds > 0 || Generators > 0 || WeaponsAccessible != WeaponAccess.None;
    }

    /// <summary>
    /// Combines World Shock (what's offered) with Trust (what's accessible).
    ///
    /// The cynical truth:
    /// - Blood buys weapons (Shock level)
    /// - Trust delivers them (Corruption level)
    /// - You need BOTH for full arsenal
    ///
    /// Config: BalanceConfig.Current.Aid, BalanceConfig.Current.Trust
    /// </summary>
    public static class AidMatrixCalculator
    {
        private const int DOUBLE_RESUPPLY_PERCENT = 200;

        private static readonly LogContext Log = new("AidMatrixCalculator");

        /// <summary>
        /// Calculate available aid based on BOTH Shock tier and Trust level.
        /// Zero-alloc: returns a stack-allocated struct.
        /// </summary>
        public static FilteredAidPackage Calculate(AidTier shockTier, TrustLevel trust)
        {
            var offered = GetOfferedAid(shockTier);
            return ApplyTrustFilter(offered, shockTier, trust);
        }

        /// <summary>
        /// Convenience method using current game state.
        /// Includes scandal penalty in trust calculation.
        /// </summary>
        public static FilteredAidPackage CalculateFromState(AidTier shockTier, float corruption, float scandalPenalty = 0f)
        {
            var trust = DonorAidCalculator.GetTrustLevel(corruption, scandalPenalty);
            return Calculate(shockTier, trust);
        }

        // ============ SHOCK TIER -> OFFERED AID ============

        private static OfferedAidPackage GetOfferedAid(AidTier tier)
        {
            return tier switch
            {
                AidTier.None => default,
                AidTier.DeepConcern => GetDeepConcernOffer(),
                AidTier.Headlines => GetHeadlinesOffer(),
                AidTier.GlobalShock => GetGlobalShockOffer(),
                _ => LogUnknownTierAndReturnDefault(tier)
            };
        }

        private static OfferedAidPackage LogUnknownTierAndReturnDefault(AidTier tier)
        {
            Log.Warn($"[AidMatrixCalculator] Unknown AidTier: {tier}, defaulting to DeepConcern");
            return GetDeepConcernOffer();
        }

        private static OfferedAidPackage GetDeepConcernOffer()
        {
            var aidCfg = BalanceConfig.Current.Aid;
            return new OfferedAidPackage
            {
                MaxFunds = aidCfg.DeepConcernFunds,
                AmmoResupplyPercent = (int)Math.Round(aidCfg.DeepConcernAmmoPercent),
                GeneratorsAvailable = 0,
                GeneratorMW = 0,
                PatriotDays = 0,
                WeaponsOffered = WeaponAccess.BasicOnly,
                GeneratorsOffered = false
            };
        }

        private static OfferedAidPackage GetHeadlinesOffer()
        {
            var aidCfg = BalanceConfig.Current.Aid;
            return new OfferedAidPackage
            {
                MaxFunds = aidCfg.HeadlinesFunds,
                AmmoResupplyPercent = 100,
                GeneratorsAvailable = aidCfg.HeadlinesGenerators,
                GeneratorMW = aidCfg.HeadlinesGeneratorMw,
                PatriotDays = 0,
                WeaponsOffered = WeaponAccess.Headlines,
                GeneratorsOffered = true
            };
        }

        // Intentionally same MW as Headlines — GlobalShock provides more generators, not higher-capacity ones
        private static OfferedAidPackage GetGlobalShockOffer()
        {
            var aidCfg = BalanceConfig.Current.Aid;
            return new OfferedAidPackage
            {
                MaxFunds = aidCfg.GlobalShockFunds,
                AmmoResupplyPercent = DOUBLE_RESUPPLY_PERCENT,
                GeneratorsAvailable = aidCfg.GlobalShockGenerators,
                GeneratorMW = aidCfg.HeadlinesGeneratorMw, // Same MW as Headlines — GlobalShock provides more generators, not higher-capacity ones
                PatriotDays = aidCfg.GlobalShockPatriotDays,
                WeaponsOffered = WeaponAccess.All,
                GeneratorsOffered = true
            };
        }

        // ============ TRUST FILTER ============

        private static FilteredAidPackage ApplyTrustFilter(OfferedAidPackage offered, AidTier shockTier, TrustLevel trust)
        {
            var trustCfg = BalanceConfig.Current.Trust;

            var result = new FilteredAidPackage
            {
                ShockTier = shockTier,
                TrustLevel = trust
            };

            switch (trust)
            {
                case TrustLevel.Full:
                    result.Funds = offered.MaxFunds;
                    result.AmmoResupplyPercent = offered.AmmoResupplyPercent;
                    result.Generators = offered.GeneratorsAvailable;
                    result.GeneratorMW = offered.GeneratorMW;
                    result.PatriotDays = offered.PatriotDays;
                    result.WeaponsAccessible = offered.WeaponsOffered;
                    result.GeneratorsAccessible = offered.GeneratorsOffered;
                    result.TrustMessage = TrustMessageId.FullAccess;
                    break;

                case TrustLevel.Partial:
                    result.Funds = (int)Math.Round(offered.MaxFunds * trustCfg.PartialDelivery);
                    result.AmmoResupplyPercent = offered.AmmoResupplyPercent;
                    result.Generators = offered.GeneratorsAvailable;
                    result.GeneratorMW = offered.GeneratorMW;
                    result.PatriotDays = 0;
                    result.WeaponsAccessible = WeaponAccess.BasicOnly;
                    result.GeneratorsAccessible = offered.GeneratorsOffered;
                    result.TrustMessage = TrustMessageId.AdvancedBlocked;
                    result.BlockedReason = BlockReasonId.RequiresFullTrust;
                    break;

                case TrustLevel.Minimal:
                    result.Funds = (int)Math.Round(offered.MaxFunds * trustCfg.MinimalDelivery);
                    result.TrustMessage = TrustMessageId.MinimalAidOnly;
                    result.BlockedReason = BlockReasonId.CorruptionTooHigh;
                    break;

                case TrustLevel.Refused:
                    result.SanctionsApplied = true;
                    result.TrustMessage = TrustMessageId.AidRefused;
                    result.BlockedReason = BlockReasonId.GovernanceRefusal;
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(TrustLevel)}: {trust}");
                    break;
            }

            // Blocked = offered but not accessible
            result.WeaponsOfferedButBlocked = offered.WeaponsOffered & ~result.WeaponsAccessible;

            return result;
        }

        // ============ CONFERENCE RESULT ============

        /// <summary>
        /// Single source of truth for conference execution.
        /// Converts FilteredAidPackage → DonorConferenceResult for the selected aid type.
        /// Replaces DonorAidCalculator.CalculateAid (which used separate Diplomacy config
        /// values that diverged from the Aid config used by the UI matrix).
        /// </summary>
        public static DonorConferenceResult ToConferenceResult(AidTier shockTier, TrustLevel trust, DonorAidType aidType)
        {
            var pkg = Calculate(shockTier, trust);

            if (trust == TrustLevel.Refused)
            {
                return new DonorConferenceResult
                {
                    Success = false,
                    TrustLevel = trust,
                    AidType = aidType,
                    SanctionDays = DonorAidCalculator.SANCTION_DAYS,
                    TradePenalty = DonorAidCalculator.SANCTION_TRADE_PENALTY,
                    DonorSpeaker = "UN Special Envoy",
                    DonorMessage = DonorMessageIds.RefusalAntiCorruptionAudit
                };
            }

            if (!pkg.HasAnyAid)
            {
                return new DonorConferenceResult
                {
                    Success = false,
                    TrustLevel = trust,
                    AidType = aidType,
                    DonorSpeaker = "Donor Coordination Desk",
                    DonorMessage = DonorMessageIds.RefusalShockUnavailable
                };
            }

            var result = new DonorConferenceResult
            {
                Success = true,
                TrustLevel = trust,
                AidType = aidType
            };

            switch (aidType)
            {
                case DonorAidType.Funds:
                    result.FundsReceived = pkg.Funds;
                    result.DonorSpeaker = trust == TrustLevel.Full ? "EU Ambassador" : "IMF Representative";
#pragma warning disable CIVIC019 // All 3 TrustMessageId values covered; discard = safe fallback for future additions
                    result.DonorMessage = pkg.TrustMessage switch
                    {
                        TrustMessageId.FullAccess => DonorMessageIds.AidFundsFull,
                        TrustMessageId.AdvancedBlocked => DonorMessageIds.AidFundsMonitoring,
                        TrustMessageId.MinimalAidOnly => DonorMessageIds.AidHumanitarianOnly,
                        _ => ""
                    };
#pragma warning restore CIVIC019
                    break;

                case DonorAidType.Power:
                    result.GeneratorsReceived = pkg.Generators;
                    result.GeneratorMW = pkg.GeneratorMW;
                    result.DonorSpeaker = "USAID Director";
                    result.DonorMessage = DonorMessageIds.AidGeneratorsDeployed;
                    break;

                case DonorAidType.Defense:
                    result.PatriotReceived = pkg.CanGetPatriot;
                    result.PatriotDays = pkg.PatriotDays;
                    if (result.PatriotReceived)
                    {
                        result.DonorSpeaker = "NATO Representative";
                        result.DonorMessage = DonorMessageIds.AidPatriotProvided;
                    }
                    else
                    {
                        result.DonorSpeaker = "Donor Coordination Desk";
                        result.DonorMessage = pkg.PatriotOfferedButBlocked
                            ? DonorMessageIds.RefusalDefenseNeedsTrust
                            : DonorMessageIds.RefusalDefenseNeedsShock;
                    }
                    break;
                default:
                    Log.Warn($"Unhandled {nameof(DonorAidType)}: {aidType}");
                    break;
            }

            // M35 FIX: Verify the chosen aid type actually delivered something
            bool hasDelivery = aidType switch
            {
                DonorAidType.Funds => result.FundsReceived > 0,
                DonorAidType.Power => result.GeneratorsReceived > 0,
                DonorAidType.Defense => result.PatriotReceived,
                _ => false
            };
            if (!hasDelivery)
            {
                result.Success = false;
                if (string.IsNullOrEmpty(result.DonorMessage))
                    result.DonorMessage = DonorMessageIds.RefusalTrustUnavailable;
            }

            return result;
        }

        // ============ UTILITY METHODS ============

        /// <summary>
        /// Check if Patriot is available (requires GlobalShock + Full Trust).
        /// </summary>
        public static bool IsPatriotAvailable(AidTier shockTier, float corruption, float scandalPenalty = 0f)
        {
            var package = CalculateFromState(shockTier, corruption, scandalPenalty);
            return package.CanGetPatriot;
        }

        /// <summary>
        /// Check if Patriot is offered but blocked by trust.
        /// </summary>
        public static bool IsPatriotBlocked(AidTier shockTier, float corruption, float scandalPenalty = 0f)
        {
            var package = CalculateFromState(shockTier, corruption, scandalPenalty);
            return package.PatriotOfferedButBlocked;
        }
    }
}
