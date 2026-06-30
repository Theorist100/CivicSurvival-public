namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Data for a social feed post.
    /// FIX NOTIF-005: Made fields readonly for immutability.
    /// </summary>
    public readonly struct SocialPost
    {
        public readonly string Author;      // @DeputatKotleta, @BabcyaZina, @InzhenerPetrenko
        public readonly string AuthorName;  // Display name
        public readonly string Message;     // The tweet text
        public readonly SocialMood Mood;    // Visual styling
        public readonly long Timestamp;     // Unix timestamp
        public readonly int DuplicateWindowSeconds;
        public readonly bool IsOfficial;    // True for NEWS (DSNS, UN, NATO), false for CHIPPER

        public SocialPost(string author, string authorName, string message, SocialMood mood, long timestamp, int duplicateWindowSeconds = 60, bool isOfficial = false)
        {
            Author = author;
            AuthorName = authorName;
            Message = message;
            Mood = mood;
            Timestamp = timestamp;
            DuplicateWindowSeconds = duplicateWindowSeconds;
            IsOfficial = isOfficial;
        }
    }
}
