namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Stable donor message identifiers. Refusal ids double as localization keys.
    /// </summary>
    public static class DonorMessageIds
    {
        public const string RefusalTrustSourceUnavailable = "DONOR_REFUSAL_TRUST_SOURCE_UNAVAILABLE";
        public const string RefusalAntiCorruptionAudit = "DONOR_REFUSAL_ANTI_CORRUPTION_AUDIT";
        public const string RefusalShockUnavailable = "DONOR_REFUSAL_SHOCK_UNAVAILABLE";
        public const string RefusalTrustUnavailable = "DONOR_REFUSAL_TRUST_UNAVAILABLE";
        public const string RefusalDefenseUnavailable = "DONOR_REFUSAL_DEFENSE_UNAVAILABLE";
        public const string RefusalDefenseNeedsTrust = "DONOR_REFUSAL_DEFENSE_NEEDS_TRUST";
        public const string RefusalDefenseNeedsShock = "DONOR_REFUSAL_DEFENSE_NEEDS_SHOCK";
        public const string RefusalGeneratorCap = "DONOR_REFUSAL_GENERATOR_CAP";
        public const string RefusalPatriotCap = "DONOR_REFUSAL_PATRIOT_CAP";
        public const string RefusalGeneric = "DONOR_REFUSAL_GENERIC";

        public const string AidFundsFull = "DONOR_AID_FUNDS_FULL";
        public const string AidFundsMonitoring = "DONOR_AID_FUNDS_MONITORING";
        public const string AidHumanitarianOnly = "DONOR_AID_HUMANITARIAN_ONLY";
        public const string AidGeneratorsDeployed = "DONOR_AID_GENERATORS_DEPLOYED";
        public const string AidPatriotProvided = "DONOR_AID_PATRIOT_PROVIDED";
        public const string AidDefenseDowngraded = "DONOR_AID_DEFENSE_DOWNGRADED";
    }
}
