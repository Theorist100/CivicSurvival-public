using System;

namespace CivicSurvival.Domains.Network.Data
{
    /// <summary>
    /// DTO for global news items received from server.
    /// Immutable record for thread safety.
    /// </summary>
    public readonly struct GlobalNewsItem
    {
        public string Id { get; }
        public string Headline { get; }
        public string Category { get; }
        public string Nickname { get; }
        public DateTime Timestamp { get; }
        public bool IsChronicle { get; }
        public string Body { get; }
        public string BreakingFlash { get; }
        public string Mood { get; }

        public GlobalNewsItem(
            string? id,
            string? headline,
            string? category,
            string? nickname,
            DateTime timestamp,
            bool isChronicle = false,
            string? body = null,
            string? breakingFlash = null,
            string? mood = null)
        {
            Id = id ?? string.Empty;
            Headline = headline ?? string.Empty;
            Category = category ?? string.Empty;
            Nickname = nickname ?? string.Empty;
            Timestamp = timestamp;
            IsChronicle = isChronicle;
            Body = body ?? string.Empty;
            BreakingFlash = breakingFlash ?? string.Empty;  // FIX BUG-NET-001: Consistent null coalescing
            Mood = mood ?? string.Empty;  // FIX BUG-NET-001: Consistent null coalescing
        }
    }

    /// <summary>
    /// Online player statistics from server.
    /// </summary>
    public readonly struct OnlineStats
    {
        public int OnlineNow { get; }
        public int OnlineHour { get; }
        public int OnlineToday { get; }
        public int TotalPlayers { get; }

        public OnlineStats(int onlineNow, int onlineHour, int onlineToday, int totalPlayers)
        {
            OnlineNow = onlineNow;
            OnlineHour = onlineHour;
            OnlineToday = onlineToday;
            TotalPlayers = totalPlayers;
        }
    }
}
