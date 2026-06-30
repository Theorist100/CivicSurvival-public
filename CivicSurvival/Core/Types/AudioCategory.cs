namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Semantic category of a CivicSurvival-emitted sound, used by the mod-audio
    /// mute settings. Source systems classify each sound they play into one of
    /// these categories and gate playback through <see cref="ModSettings.IsAudioMuted"/>.
    ///
    /// Lives in Core so the source systems (ThreatAudioOrchestrator in the ThreatUI
    /// domain, OminousSignsSystem in the Scenario domain) share the type without a
    /// domain-to-domain dependency (Axiom 5).
    /// </summary>
    public enum AudioCategory
    {
        /// <summary>Sentinel so default(AudioCategory) is not a real category (never muted).</summary>
        None = 0,

        /// <summary>Continuous drone buzz (looped 3D AudioSource).</summary>
        Drone = 1,

        /// <summary>Air-raid sirens and pre-war ominous-sign atmosphere SFX.</summary>
        Alert = 2,

        /// <summary>AA fire / intercepts and explosions (including collapse/lightning SFX).</summary>
        Combat = 3
    }
}
