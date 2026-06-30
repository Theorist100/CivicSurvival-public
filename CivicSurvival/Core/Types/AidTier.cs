namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Aid tier based on World Shock level.
    /// Determines what the world OFFERS (not what you can ACCESS - that's Trust).
    /// </summary>
    public enum AidTier : byte
    {
        None = 0,           // Default (uninitialized)
        DeepConcern = 1,    // 0-30%:  "We express concern..."
        Headlines = 2,      // 30-60%: "International community stands with..."
        GlobalShock = 3     // 60%+:   "The world is horrified..."
    }
}
