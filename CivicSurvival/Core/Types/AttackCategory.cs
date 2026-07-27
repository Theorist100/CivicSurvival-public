namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Attack categories — the axis selector for a counter-strike.
    /// Kinetic targets the enemy's Physical axis, Cyber the Digital axis,
    /// Psyops the Social axis (mirror of the city's three stability axes).
    /// </summary>
    public enum AttackCategory : byte
    {
        /// <summary>Physical attacks (drones). Lowers the enemy Physical axis.</summary>
        Kinetic = 0,

        /// <summary>Cyber attacks (grid hacking). Lowers the enemy Digital axis.</summary>
        Cyber = 1,

        /// <summary>Psychological operations (disinfo). Lowers the enemy Social axis.</summary>
        Psyops = 2
    }
}
