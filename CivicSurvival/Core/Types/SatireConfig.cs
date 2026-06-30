namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Configuration for a satire message trigger.
    /// Each domain defines these for their events.
    ///
    /// Example usage in provider:
    ///   ["THREAT_IMPACT"] = new SatireConfig("SATIRE_THREAT_IMPACT", 5, "CITIZEN", SocialMood.Suffering)
    /// </summary>
    public readonly struct SatireConfig
    {
        /// <summary>Base localization key (e.g., "SATIRE_BLACKOUT").</summary>
        public readonly string BaseLocalizationKey;

        /// <summary>Number of message variants (1-N).</summary>
        public readonly int VariantCount;

        /// <summary>Character group ID from PersonaRegistry (e.g., "TECH_WORKER", "BABCYA"). Empty for anonymous.</summary>
        public readonly string AuthorId;

        /// <summary>Mood for social post styling.</summary>
        public readonly SocialMood Mood;

        public SatireConfig(string baseLocKey, int variantCount, string authorId, SocialMood mood = SocialMood.Neutral)
        {
            BaseLocalizationKey = baseLocKey ?? "";
            VariantCount = variantCount;
            AuthorId = authorId ?? "";
            Mood = mood;
        }

        public bool HasAuthor => !string.IsNullOrEmpty(AuthorId);

        public static readonly SatireConfig Empty = new("", 0, "");
    }
}
