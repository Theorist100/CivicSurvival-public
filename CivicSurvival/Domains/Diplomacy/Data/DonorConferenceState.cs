using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Diplomacy.Data
{
    // TrustLevel enum moved to Core/Types/TrustLevel.cs for DIP compliance

    /// <summary>
    /// Type of aid the player can request from donors.
    /// </summary>
    public enum DonorAidType
    {
        /// <summary>Direct money injection to city budget.</summary>
        Funds = 0,

        /// <summary>Mobile generators (MW boost).</summary>
        Power = 1,

        /// <summary>Patriot battery placement credit (permanent, donor gift).</summary>
        Defense = 2
    }

    /// <summary>
    /// Conference status for UI display.
    /// </summary>
    public enum ConferenceStatus
    {
        Available = 0,
        Cooldown,
        NoUsesRemaining,
        CrisisTooLow,
        TooEarlyInGame,
        Sanctions,
        TrustSourceUnavailable
    }

    /// <summary>
    /// Result of calling a donor conference.
    /// </summary>
    public struct DonorConferenceResult
    {
        public bool Success;
        public TrustLevel TrustLevel;
        public DonorAidType AidType;
        // FIX S6-06: long to match CityBudgetService.AddFunds pipeline
        public long FundsReceived;
        public int GeneratorsReceived;
        public int GeneratorMW;
        public bool PatriotReceived;
        public int PatriotDays;
        public int SanctionDays;
        public float TradePenalty;
        public string DonorMessage;
        public string DonorSpeaker;

        public static DonorConferenceResult Empty => new()
        {
            DonorMessage = string.Empty,
            DonorSpeaker = string.Empty
        };
    }

    /// <summary>
    /// Persistent state for donor conference system.
    /// </summary>
    public struct DonorConferenceStateData
    {
        /// <summary>Remaining conference calls. Legal range: 0..configured max uses.</summary>
        public int UsesRemaining;
        /// <summary>Cooldown in days. Legal range: 0..MAX_DONOR_DAYS.</summary>
        public float CooldownDaysRemaining;
        /// <summary>Active donor generators. Legal range: 0..MAX_GENERATORS.</summary>
        public int ActiveGenerators;
        /// <summary>True only while SanctionDaysRemaining is positive.</summary>
        public bool SanctionsActive;
        /// <summary>Remaining sanctions duration in days. Legal range: 0..MAX_DONOR_DAYS.</summary>
        public float SanctionDaysRemaining;
        /// <summary>Trade penalty multiplier. Legal range: 0..1.</summary>
        public float TradePenalty;
        /// <summary>MW per generator at time of award. -1 = unknown or partial-save fallback.</summary>
        public int GeneratorMW;

        public static DonorConferenceStateData CreateDefault(int maxUses)
        {
            return new DonorConferenceStateData
            {
                UsesRemaining = Math.Max(0, maxUses),
                GeneratorMW = -1
            };
        }

        public void ApplySanction(float days, float tradePenalty)
        {
            SanctionDaysRemaining = Math.Max(0f, days);
            SanctionsActive = SanctionDaysRemaining > 0f;
            TradePenalty = SanctionsActive ? Math.Min(Math.Max(tradePenalty, 0f), 1f) : 0f;
        }

        public void ClearSanctions()
        {
            SanctionsActive = false;
            SanctionDaysRemaining = 0f;
            TradePenalty = 0f;
        }

        public void ClampInvariants(int maxUses, int maxGenerators, float maxDays, float maxTradePenalty)
        {
            UsesRemaining = Math.Min(Math.Max(UsesRemaining, 0), Math.Max(0, maxUses));
            CooldownDaysRemaining = Math.Min(Math.Max(CooldownDaysRemaining, 0f), maxDays);
            ActiveGenerators = Math.Min(Math.Max(ActiveGenerators, 0), Math.Max(0, maxGenerators));
            SanctionDaysRemaining = Math.Min(Math.Max(SanctionDaysRemaining, 0f), maxDays);
            TradePenalty = Math.Min(Math.Max(TradePenalty, 0f), maxTradePenalty);
            GeneratorMW = Math.Max(-1, GeneratorMW);

            if (!SanctionsActive || SanctionDaysRemaining <= 0f)
                ClearSanctions();
        }
    }
}
