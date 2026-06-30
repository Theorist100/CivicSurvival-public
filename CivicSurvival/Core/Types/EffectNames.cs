namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// SFX/VFX effect name constants for use across domains.
    /// Actual prefab loading handled by EffectCacheSystem.
    /// One-shot VFX explosions use VanillaVfxSystem (direct EnabledEffectData injection
    /// into vanilla VFX Graph); the attached ballistic exhaust combines both paths —
    /// a prefab Effect element (pose offsets) plus a manually injected owner-attached
    /// record (VanillaVfxSystem.TryAttachEffect).
    /// </summary>
    public static class EffectNames
    {
        // === SFX ===
        public const string SIREN_LOOP = "EarlyDisasterWarningLoopSFX";
        public const string COLLAPSE_SFX = "BuildingCollapseSFX";
        public const string LIGHTNING_SFX = "LightningSFX";
        public const string FIRE_LOOP = "FireLoopSFX";

        // === VFX ===
        /// <summary>Ballistic nozzle exhaust — vanilla medium fire authored for a MOVING
        /// burning object (a separate asset from FireMedium, which vanilla uses on stationary
        /// buildings), so it fits a flying missile better and reads brighter than the earlier
        /// FireSmallVFX. Owner-attached via VanillaVfxSystem.TryAttachEffect.</summary>
        public const string FIRE_MOVING_MEDIUM_VFX = "FireMovingMediumVFX";
    }
}
