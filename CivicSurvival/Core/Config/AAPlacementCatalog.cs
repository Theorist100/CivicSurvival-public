using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// One way to field an AA: a gun barrel (<see cref="AAType"/>, which carries the stats) plus the
    /// source it is acquired through (<see cref="AAPlacementMode"/>, which carries the price). The
    /// two are separate axes — the same prefab arrives worn-out and free from the city's reserves, or
    /// standard and paid for, and the Patriot arrives either bought or donated.
    ///
    /// Presentation only — deliberately NOT folded into <see cref="AATypeParams"/>, which is read on
    /// the gameplay/visual hot path (per engagement, per tracer) and must not grow string fields.
    /// Balance values stay in <see cref="AAParams.ForType"/>; this says what the option is called and
    /// what it looks like.
    /// </summary>
    public readonly struct AAPlacementOption
    {
        /// <summary>The barrel — supplies stats via <see cref="AAParams.ForType"/> and the deployed count.</summary>
        public readonly AAType Type;

        /// <summary>How it is acquired and paid for.</summary>
        public readonly AAPlacementMode Mode;

        /// <summary>Prefab id sent through the placement trigger. Options sharing a prefab are the
        /// same barrel from different sources, and the panel groups them under one tile.</summary>
        public readonly string Prefab;

        /// <summary>Locale key for the display name.</summary>
        public readonly string NameKey;

        /// <summary>Thumbnail path relative to the icon host.</summary>
        public readonly string Icon;

        public AAPlacementOption(AAType type, AAPlacementMode mode, string prefab, string nameKey, string icon)
        {
            Type = type;
            Mode = mode;
            Prefab = prefab;
            NameKey = nameKey;
            Icon = icon;
        }
    }

    /// <summary>
    /// Every AA the player can field, as data. The panel renders this list — it does not know the
    /// roster, so a new AA becomes one row here (plus its arm in <see cref="AAParams.ForType"/> and
    /// its balance keys) and appears in the UI with no TypeScript touched at all.
    ///
    /// Previously this roster was spelled out twice in the panel — once per hand-written build card
    /// and once per named DTO field pair — which is why adding a gun used to mean editing C#, the
    /// wire contract and the React tree in lockstep.
    /// </summary>
    public static class AAPlacementCatalog
    {
        public const string BoforsPrefab = "AA_40mm_Bofors";
        public const string GepardPrefab = "Gepard";
        public const string PatriotPrefab = "MIM104_SAM";
        public const string HawkPrefab = "KrazHawk";

        public static readonly IReadOnlyList<AAPlacementOption> All = new List<AAPlacementOption>
        {
            // The same Bofors barrel from two sources: the reserve grant is worn out (its own AAType,
            // hence its own weaker stats) while the bought one is standard issue.
            new(AAType.HeritageBofors, AAPlacementMode.Heritage, BoforsPrefab, "UI_AA_HERITAGE_BOFORS", "buildings/40mm.jpg"),
            new(AAType.Bofors40mm, AAPlacementMode.Paid, BoforsPrefab, "UI_AA_BOFORS_L60", "buildings/40mm.jpg"),
            new(AAType.Gepard, AAPlacementMode.Paid, GepardPrefab, "UI_AA_GEPARD", "buildings/Gepard.jpg"),

            // Hawk — allied-donated missile anti-drone (like the Patriot, it arrives on a donor
            // credit or bought), cheaper than the Patriot and specialised against drones. Listed
            // before the Patriot so the tile order runs guns → Hawk → Patriot (the premium last).
            new(AAType.HawkSAM, AAPlacementMode.DonorCredit, HawkPrefab, "UI_AA_HAWK", "buildings/Hawk.jpg"),
            new(AAType.HawkSAM, AAPlacementMode.Paid, HawkPrefab, "UI_AA_HAWK", "buildings/Hawk.jpg"),

            // One Patriot barrel, two sources: a donor credit spends no money, buying one does.
            new(AAType.PatriotSAM, AAPlacementMode.DonorCredit, PatriotPrefab, "UI_AA_PATRIOT_SAM", "buildings/Patriot.jpg"),
            new(AAType.PatriotSAM, AAPlacementMode.Paid, PatriotPrefab, "UI_AA_PATRIOT_SAM", "buildings/Patriot.jpg"),

            // Off the books: the same barrels, tuned past spec, for shadow money. Only the bought
            // ones have a market twin — the reserve grant is a barrel the city already owns, and
            // nobody smuggles you a museum piece.
            new(AAType.Bofors40mm, AAPlacementMode.BlackMarket, BoforsPrefab, "UI_AA_BOFORS_L60", "buildings/40mm.jpg"),
            new(AAType.Gepard, AAPlacementMode.BlackMarket, GepardPrefab, "UI_AA_GEPARD", "buildings/Gepard.jpg"),
            new(AAType.HawkSAM, AAPlacementMode.BlackMarket, HawkPrefab, "UI_AA_HAWK", "buildings/Hawk.jpg"),
            new(AAType.PatriotSAM, AAPlacementMode.BlackMarket, PatriotPrefab, "UI_AA_PATRIOT_SAM", "buildings/Patriot.jpg"),
        }.AsReadOnly();
    }
}
