namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Data structure for satirical character persona.
    /// Used by PersonaRegistry.
    /// </summary>
    public readonly struct PersonaProfile
    {
        /// <summary>Unique identifier (e.g., "TECH_WORKER", "BABCYA").</summary>
        public readonly string Id;

        /// <summary>Social media handle (e.g., "@InzhenerPetrenko").</summary>
        public readonly string Handle;

        /// <summary>Localization key prefix for this persona's messages.</summary>
        public readonly string MessageKeyPrefix;

        public PersonaProfile(string id, string handle, string messageKeyPrefix)
        {
            Id = id;
            Handle = handle;
            MessageKeyPrefix = messageKeyPrefix;
        }
    }
}
