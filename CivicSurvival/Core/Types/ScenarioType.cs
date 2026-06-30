namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Scenario type based on city population at start.
    /// Determines narrative flow and game mechanics.
    /// </summary>
    public enum ScenarioType : byte
    {
        /// <summary>Default (uninitialized).</summary>
        None = 0,

        /// <summary>Population less than 1,000. Pre-war buildup, then refugee wave.</summary>
        Village = 1,

        /// <summary>Population 1,000 - 10,000. Brief warning, then Day 0.</summary>
        Town = 2,

        /// <summary>Population over 10,000. Immediate Day 0 shock.</summary>
        City = 3
    }
}
