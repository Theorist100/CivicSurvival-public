namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Narrative tone for Telemarathon broadcasts.
    /// </summary>
    public enum NarrativeMode : byte
    {
        /// <summary>"Warm bath" - calming content. Reduces panic but trust decays. RISKY during attacks!</summary>
        Soothing = 0,

        /// <summary>Alert mode - preparing citizens. Boosts vigilance but causes stress.</summary>
        Alarmist = 1,

        /// <summary>Honest reporting - matches reality. Recovers trust but may increase panic.</summary>
        Realistic = 2
    }
}
