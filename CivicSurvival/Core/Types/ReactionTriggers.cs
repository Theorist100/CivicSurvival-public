namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Event triggers for character reactions.
    /// </summary>
    public static class ReactionTriggers
    {
        // Lifecycle
        public const string OnBind = "OnBind";
        public const string OnBuildingDestroyed = "OnBuildingDestroyed";
        public const string OnLeaving = "OnLeaving";

        // Blackout events
        public const string OnBlackout = "OnBlackout";
        public const string OnBlackoutLong = "OnBlackoutLong";
        public const string OnBlackoutExtreme = "OnBlackoutExtreme";
        public const string OnPowerRestored = "OnPowerRestored";

        // Corruption events
        public const string OnCorruptionHigh = "OnCorruptionHigh";
        public const string OnShadowExport = "OnShadowExport";
        public const string OnVIPProtection = "OnVIPProtection";

        // Investigation events
        public const string OnInvestigationStart = "OnInvestigationStart";
        public const string OnInvestigationProgress = "OnInvestigationProgress";
        public const string OnArticlePublished = "OnArticlePublished";
        public const string OnPoliceInvolved = "OnPoliceInvolved";
        public const string OnArrest = "OnArrest";

        // Protest events
        public const string OnProtestSmall = "OnProtestSmall";
        public const string OnProtestLarge = "OnProtestLarge";

        // Idle/ambient
        public const string Idle = "Idle";
        public const string IdleWaiting = "IdleWaiting";

        // Valera-specific triggers
        public const string OnGeneratorNearby = "OnGeneratorNearby";
        public const string OnAlert = "OnAlert";

        // Milestone events
        public const string OnWarFatigue = "OnWarFatigue";      // Day 180
        public const string OnVictory = "OnVictory";            // Day 365
    }
}
