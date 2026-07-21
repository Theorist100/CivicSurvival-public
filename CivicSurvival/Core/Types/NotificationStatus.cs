namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Semantic status for notifications - determines visual styling.
    /// NotificationSystem uses this instead of parsing localized text.
    /// </summary>
    public enum NotificationStatus
    {
        /// <summary>
        /// Informational (blue/neutral) - general updates.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Success (green) - power restored, threat intercepted, repairs complete.
        /// </summary>
        Success = 1,

        /// <summary>
        /// Warning (yellow/orange) - alerts, low resources, rising tension.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Error (red) - damage, failures, critical situations.
        /// </summary>
        Error = 3
    }
}
