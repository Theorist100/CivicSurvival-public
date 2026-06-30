using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Diplomacy.Data;

namespace CivicSurvival.Domains.Diplomacy.Logic
{
    /// <summary>
    /// Pure static service for calculating donor aid amounts.
    /// No dependencies - testable.
    /// </summary>
    public static class DonorAidCalculator
    {
        // Sanctions and Patriot constants — used by AidMatrixCalculator.ToConferenceResult
        public static int PATRIOT_DAYS => BalanceConfig.Current.Diplomacy.PatriotDays;
        public static float SANCTION_TRADE_PENALTY => BalanceConfig.Current.Diplomacy.SanctionTradePenalty;
        public static int SANCTION_DAYS => BalanceConfig.Current.Diplomacy.SanctionDays;

        /// <summary>
        /// Calculate Trust Level from Corruption Score and Scandal Penalty.
        /// Scandal adds to effective corruption from international perspective.
        /// Uses BalanceConfig.Current.Trust thresholds.
        /// </summary>
        public static TrustLevel GetTrustLevel(float corruptionScore, float scandalPenalty = 0f)
        {
            var trust = BalanceConfig.Current.Trust;
            int trustIndex = GetTrustIndex(corruptionScore, scandalPenalty);

            int fullMin = (int)System.Math.Ceiling(100f - trust.FullAidMax);
            int partialMin = (int)System.Math.Ceiling(100f - trust.PartialAidMax);
            int minimalMin = (int)System.Math.Ceiling(100f - trust.MinimalAidMax);

            if (trustIndex >= fullMin) return TrustLevel.Full;
            if (trustIndex >= partialMin) return TrustLevel.Partial;
            if (trustIndex >= minimalMin) return TrustLevel.Minimal;
            return TrustLevel.Refused;
        }

        /// <summary>
        /// Get trust index (0-100) from corruption score.
        /// Optionally includes scandal penalty for international scrutiny.
        /// </summary>
        public static int GetTrustIndex(float corruptionScore, float scandalPenalty = 0f)
        {
            float rawTrust = 100f - corruptionScore - scandalPenalty;
            return (int)System.Math.Min(100f, System.Math.Max(0f, rawTrust));
        }

        /// <summary>
        /// Check if specific aid type is available at trust level.
        /// </summary>
        /// W6-01 FIX: shockTier is MANDATORY — no default parameter.
        /// Compiler will reject any caller that doesn't provide a real ShockTier.
        public static bool IsAidAvailable(TrustLevel trust, DonorAidType aidType, AidTier shockTier)
        {
            return aidType switch
            {
                DonorAidType.Funds => trust != TrustLevel.Refused,
                DonorAidType.Power => (trust == TrustLevel.Full || trust == TrustLevel.Partial)
                    && shockTier >= AidTier.Headlines,
                DonorAidType.Defense => trust == TrustLevel.Full && shockTier == AidTier.GlobalShock,
                _ => false
            };
        }

        // GetFundsAmount, GetGeneratorCount, GetGeneratorMW, CalculateAid — REMOVED.
        // All amount calculations now go through AidMatrixCalculator (shock + trust → amounts).
        // DonorAidCalculator retains trust utilities only (GetTrustLevel, IsAidAvailable, constants).

        /// <summary>
        /// Get expected aid description for UI.
        /// Accepts pre-calculated package to avoid redundant AidMatrixCalculator.Calculate call.
        /// </summary>
        public static string GetExpectedAidDescription(TrustLevel trust, in FilteredAidPackage pkg)
        {
            if (trust == TrustLevel.Refused) return "REFUSED (sanctions)";

            int totalMW = pkg.Generators * pkg.GeneratorMW;

            if (trust == TrustLevel.Full)
                return $"Full (${pkg.Funds / 1000}k / {totalMW}MW{(pkg.CanGetPatriot ? " / Patriot" : "")})";
            if (trust == TrustLevel.Partial)
                return $"Partial (${pkg.Funds / 1000}k / {totalMW}MW)";
            return $"Minimal (${pkg.Funds / 1000}k only)";
        }
    }
}
