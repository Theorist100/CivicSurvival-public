using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Network.Data
{
    /// <summary>
    /// Official news item rendered by Herald. Separate from SocialPost so NEWS can
    /// keep its own retention and contract instead of sharing Chipper's social feed.
    /// </summary>
    public readonly struct NewsFeedPost
    {
        public readonly string Id;
        public readonly string Source;
        public readonly string Title;
        public readonly string Body;
        public readonly SocialMood Mood;
        public readonly long Timestamp;
        public readonly string Category;

        /// <summary>
        /// Origin of the post: "global" (server-wide Chronicle/breaking),
        /// "personal" (player's own Personal Chronicle), or "reactive" (event-driven).
        /// UI uses this to badge personal/reactive entries distinctly (Phase 4).
        /// </summary>
        public readonly string Scope;

        /// <summary>
        /// True when the post text was produced by AI generation (the global/personal
        /// Chronicle digests). False for hardcoded reactive narrative bulletins
        /// (NewsPostEvent / EmitNews) and server breaking news. The UI shows an "AI"
        /// badge so the player can tell machine-written entries from authored ones.
        /// </summary>
        public readonly bool IsAiGenerated;

        public NewsFeedPost(
            string id,
            string source,
            string title,
            string body,
            SocialMood mood,
            long timestamp,
            string category,
            string scope = "global",
            bool isAiGenerated = false)
        {
            Id = id ?? string.Empty;
            Source = source ?? string.Empty;
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            Mood = mood;
            Timestamp = timestamp;
            Category = category ?? string.Empty;
            Scope = scope ?? "global";
            IsAiGenerated = isAiGenerated;
        }

        public static NewsFeedPost FromGlobalNews(GlobalNewsItem item, SocialMood mood, string body)
        {
            string category = item.Category;
            if (string.IsNullOrEmpty(category))
                category = item.IsChronicle ? "chronicle" : "breaking";

            return new NewsFeedPost(
                id: item.Id,
                source: item.IsChronicle ? "The Chronicle" : $"Global: {item.Nickname}",
                title: item.Headline,
                body: body,
                mood: mood,
                timestamp: ToUnixSeconds(item.Timestamp),
                category: category,
                scope: "global",
                // The Chronicle digest is AI-written; server "breaking" items are authored.
                isAiGenerated: item.IsChronicle);
        }

        /// <summary>
        /// Build a post from a player's Personal Chronicle digest item (Mode A).
        /// Same wire shape as a global chronicle item; differs only in Scope/Source
        /// so the existing NewsFeedService aggregates both into one feed.
        /// </summary>
        public static NewsFeedPost FromPersonalChronicle(GlobalNewsItem item, SocialMood mood, string body)
        {
            string category = item.Category;
            if (string.IsNullOrEmpty(category))
                category = "personal";

            return new NewsFeedPost(
                id: item.Id,
                source: "Your Chronicle",
                title: item.Headline,
                body: body,
                mood: mood,
                timestamp: ToUnixSeconds(item.Timestamp),
                category: category,
                scope: "personal",
                // Personal Chronicle is an AI-generated digest of the player's own run.
                isAiGenerated: true);
        }

        private static long ToUnixSeconds(DateTime timestamp)
        {
            if (timestamp == DateTime.MinValue)
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var utc = timestamp.Kind == DateTimeKind.Utc
                ? timestamp
                : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }
    }
}
