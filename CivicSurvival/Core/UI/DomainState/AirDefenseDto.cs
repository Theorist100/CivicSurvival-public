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
        public int AaStations;
        public bool SirenActive;

        // Ammo by MUNITION KIND, not by gun type — the axis the player actually decides on.
        // Rockets are dear and gate-limited; shells are cheap and bought in bulk. Between a 40mm
        // Bofors and a 35mm Gepard there is no decision at all, so they share one line: both are
        // cheap, both answer drones, and both are already restocked by one button.
        //
        // The kind split mirrors the two resupply buttons that already exist — this pair of
        // counters IS what GunsResupplyCost/PatriotResupplyCost have always priced. Ammo itself
        // stays per-installation (AirDefenseInstallation.CurrentAmmo); these are display sums
        // over the installations of each kind, taken from the AAType-keyed stats snapshot.
        //
        // A third kind earns its line only when a gun arrives with a genuinely different economy
        // (a MANPADS with a cheap rocket, say) — never merely because a new gun was added.
        public int PatriotAmmo;
        public int PatriotMaxAmmo;
        public int PatriotResupplyCost;
        /// <summary>Shells across every gun type (Bofors/Gepard/Heritage), summed over their
        /// installations.</summary>
        public int GunsAmmo;
        public int GunsMaxAmmo;
        /// <summary>Summed flat cost of restocking every gun type (Bofors/Gepard/Heritage) that
        /// currently has a deficit — the price on the single "restock guns" button.</summary>
        public int GunsResupplyCost;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanResupplyPatriot), "ResupplyPatriotLockedReasonId")]
        public bool CanResupplyPatriot;
        [Attributes.DtoEligibility(typeof(AirDefenseEligibility), nameof(AirDefenseEligibility.CanResupplyGuns), "ResupplyGunsLockedReasonId")]
        public bool CanResupplyGuns;

        /// <summary>
        /// Every way the player can field an AA right now, one row per (barrel × source) — the panel
        /// renders this list and knows no roster of its own. Pre-serialized JSON array of
        /// <see cref="AaPlacementOptionEntry"/>, built from <see cref="Config.AAPlacementCatalog"/>.
        ///
        /// A row carries its own stats, price, affordability and can/reason pair, because those
        /// differ per source: the same Bofors barrel is free and worn from the reserves, or standard
        /// and paid for. This replaces ~20 named fields that spelled the roster out by hand and had
        /// to grow by six every time an AA was added.
        /// </summary>
        public string AaRosterJson;

        // Heritage AA
        public int HeritageCredits;
        public int HeritageCreditsMax;
        /// <summary>Global toggle: do Patriot SAMs engage drones (Shahed)? Default false —
        /// Patriot is reserved for ballistics unless the player opts in.</summary>
        public bool PatriotInterceptsDrones;
        /// <summary>Per-save AA rule: auto-buy ammo during calm (trickle refill)? Default true —
        /// when off, AA is refilled only via the manual emergency resupply.</summary>
        public bool AutoResupplyEnabled;

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
