namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One way to field an AA — a barrel plus the source it is acquired through — as the defense
    /// panel sees it. Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns
    /// the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// Rows sharing a <see cref="Prefab"/> are the same barrel from different sources and the panel
    /// groups them under one tile: stats, price and gate all differ per source, so each row carries
    /// its own — including its can/reason pair, which is why placement needs no DTO-level gates.
    /// </summary>
    public partial struct AaPlacementOptionEntry
    {
        /// <summary>Prefab id to send through the placement trigger; also the tile grouping key.</summary>
        public string Prefab;

        /// <summary>AAPlacementMode — how it is acquired and paid for.</summary>
        public int Mode;

        /// <summary>Locale key for the display name.</summary>
        public string NameKey;

        /// <summary>Thumbnail path relative to the icon host.</summary>
        public string Icon;

        /// <summary>AmmoKind this barrel eats — which restock button keeps it firing.</summary>
        public int Munition;

        public float Range;
        public float InterceptShahed;
        public float InterceptBallistic;
        public int Crew;

        /// <summary>Live installations of this barrel right now.</summary>
        public int Deployed;

        /// <summary>Money price. 0 for credit-funded options — they spend a grant, not cash.</summary>
        public int Cost;

        /// <summary>Grants left for a credit-funded option; -1 when the option is bought with money.
        /// Lets the panel tell an exhausted grant (0) from a plain purchase.</summary>
        public int CreditsLeft;

        /// <summary>How many the player could field right now — funds/price bound by manpower/crew.</summary>
        public int AffordableCount;

        public bool CanPlace;
        public string LockedReasonId;
    }
}
