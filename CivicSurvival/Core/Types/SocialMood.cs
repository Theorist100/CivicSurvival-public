namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Mood affects visual styling of social posts.
    /// </summary>
    public enum SocialMood
    {
        Neutral = 0,    // Gray
        Smug = 1,       // Gold (Kotleta bragging)
        Suffering = 2,  // Blue (Babcya complaining)
        Warning = 3,    // Orange (Petrenko whistleblowing)
        Angry = 4,      // Red (Protests)
        Suspicious = 5, // Purple (Investigation)
        Paranoid = 6    // Green/tinfoil (Valera's conspiracy posts)
    }
}
