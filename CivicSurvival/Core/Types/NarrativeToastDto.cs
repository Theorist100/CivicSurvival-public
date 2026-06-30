namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Transit DTO for the toast pipeline. Resolvers create these, NotificationSystem
    /// renders them as vanilla toasts. News (Herald) and social (CHIPPER) no longer
    /// flow through this DTO — they are published directly as NewsPostEvent /
    /// SocialPostEvent, so Channel is always SystemAlert.
    /// </summary>
    /// <param name="Channel">Always SystemAlert (vanilla toast).</param>
    /// <param name="Id">Unique ID (TimedId) for cooldown tracking</param>
    /// <param name="Title">Toast title</param>
    /// <param name="Message">Notification content</param>
    /// <param name="Mood">Visual mood (retained for the DTO contract)</param>
    /// <param name="Status">Semantic status for visual styling (replaces text parsing)</param>
    public record NarrativeToastDto(
        NotificationType Channel,
        string Id,
        string Title,
        string Message,
        SocialMood Mood = SocialMood.Neutral,
        NotificationStatus Status = NotificationStatus.Info
    );
}
