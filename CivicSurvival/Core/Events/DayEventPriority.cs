namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Priority tiers for DayChangedEvent dispatch ordering.
    /// Lower number = dispatched first.
    ///
    /// Income before Cost ensures ECB-based income is processed
    /// before ECB-based deductions in the same ShadowWalletSystem pass.
    /// Narrative last — OminousSignsSystem may trigger ActChanged (war start).
    /// </summary>
    public static class DayEventPriority
    {
        public const int Income = 10;
        public const int Cost = 20;
        public const int Cleanup = 40;   // L-104: Structural cleanup (district removal) before StateChange readers
        public const int StateChange = 50;
        public const int Narrative = 90;
    }
}
