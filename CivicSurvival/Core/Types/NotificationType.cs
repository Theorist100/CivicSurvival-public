namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Type of notification determines where it's displayed. Since the news/social
    /// channel split, narrative posts are published directly as NewsPostEvent /
    /// SocialPostEvent; only system-alert toasts travel through NotificationState,
    /// so this enum has a single member kept for the NarrativeToastDto contract.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// System alerts (blackouts, failures, winter) → Vanilla toast UI (right side)
        /// Red/yellow urgent notifications.
        /// </summary>
        SystemAlert = 0
    }
}
