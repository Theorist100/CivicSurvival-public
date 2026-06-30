using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Air defense domain DTO.
    /// EnergyFocusRange, InfraFocusRange, ResidentialFocusRange, TimeEstimate
    /// are pre-serialized JSON objects.
    /// </summary>
    public partial struct AirDefenseDto : IDomainDto
    {
        // AA stations
        public int AaAmmo;
        public int AaMaxAmmo;
        public int AaStations;
        public bool SirenActive;

        // Per-AAType ammo (curr/max summed over installations of that type). Each type still
        // shows its own bar; the resupply BUTTON is consolidated — one "restock guns" button for
        // all gun types and a separate Patriot button (Patriot has a dear flat cost + cooldown and
        // is excluded from the calm-phase auto refill).
        public int PatriotAmmo;
        public int PatriotMaxAmmo;
        public int PatriotResupplyCost;
        public int BoforsAmmo;
        public int BoforsMaxAmmo;
        public int HeritageAmmo;
        public int HeritageMaxAmmo;
        public int GepardAmmo;
        public int GepardMaxAmmo;
        /// <summary>Summed flat cost of restocking every gun type (Bofors/Gepard/Heritage) that
        /// currently has a deficit — the price on the single "restock guns" button.</summary>
        public int GunsResupplyCost;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanResupplyPatriot), "ResupplyPatriotLockedReasonId")]
        public bool CanResupplyPatriot;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanResupplyGuns), "ResupplyGunsLockedReasonId")]
        public bool CanResupplyGuns;

        // Heritage AA
        public int HeritageCredits;
        public int HeritageCreditsMax;
        public int HeritageCrew;
        public int BoforsCrew;
        public int GepardCrew;
        public int HeritageBoforsCount;
        public int BoforsCount;
        public int GepardCount;
        public int PatriotCount;
        public int BoforsPrice;
        public int GepardPrice;
        public int PatriotPrice;
        public int PatriotCrew;
        /// <summary>Global toggle: do Patriot SAMs engage drones (Shahed)? Default false —
        /// Patriot is reserved for ballistics unless the player opts in.</summary>
        public bool PatriotInterceptsDrones;
        /// <summary>Per-save AA rule: auto-buy ammo during calm (trickle refill)? Default true —
        /// when off, AA is refilled only via the manual emergency resupply.</summary>
        public bool AutoResupplyEnabled;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanPlaceHeritageBofors), "HeritageBoforsLockedReasonId")]
        public bool CanPlaceHeritageBofors;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanPlaceDonorPatriot), "DonorPatriotLockedReasonId")]
        public bool CanPlaceDonorPatriot;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanPlacePaidBofors), "PaidBoforsLockedReasonId")]
        public bool CanPlacePaidBofors;
        /// <summary>How many paid Bofors the player can field right now — the binding limit of
        /// available funds (/price) and free manpower (/crew). 0 = can't place any.</summary>
        public int PaidBoforsAffordableCount;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanPlacePaidGepard), "PaidGepardLockedReasonId")]
        public bool CanPlacePaidGepard;
        /// <summary>How many paid Gepards the player can field right now — the binding limit of
        /// available funds (/price) and free manpower (/crew). 0 = can't place any.</summary>
        public int PaidGepardAffordableCount;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanPlacePaidPatriot), "PaidPatriotLockedReasonId")]
        public bool CanPlacePaidPatriot;
        /// <summary>How many paid Patriots the player can field right now — the binding limit of
        /// available funds (/price) and free manpower (/crew). 0 = can't place any.</summary>
        public int PaidPatriotAffordableCount;

        // Defense policy
        public string DefensePolicyName;
        public int DefensePolicyId;
        public int SpotterPenaltyPercent;

        // Donor Patriot credits
        public int DonorPatriotCredits;

        public string EmergencyResupplyRequestJson;
        public string DefensePolicyRequestJson;
        public string PatriotDroneToggleRequestJson;
        public string AirDefensePlacementRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
