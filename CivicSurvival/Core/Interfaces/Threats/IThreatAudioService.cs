using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Interfaces.Threats
{
    /// <summary>
    /// Service for threat-related audio orchestration.
    /// Controls air raid siren and provides audio state for UI.
    ///
    /// Implementor: ThreatAudioOrchestrator (Threats domain)
    /// Consumers: IntroScenarioSystem (Scenario domain), AirDefenseUIPanel (AirDefense domain)
    /// Null-object: void siren/sound methods are no-ops, GetAudioState returns
    /// default((0, 0f, false)) = no threats, no distance, siren inactive.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ThreatUIName)]
    public interface IThreatAudioService
    {
        /// <summary>
        /// Force start air raid siren (for intro/cinematic sequences).
        /// Overrides automatic phase-based control.
        /// </summary>
        void ForceStartSiren();

        /// <summary>
        /// Force stop air raid siren (for skip intro).
        /// </summary>
        void ForceStopSiren();

        /// <summary>
        /// Get audio state for UI visualization.
        /// </summary>
        /// <returns>Tuple of (active threat count, closest threat distance, siren active flag)</returns>
        (int threatCount, float closestDistance, bool sirenActive) GetAudioState();

        /// <summary>
        /// Play intercept sound effect (AA fire + explosion).
        /// </summary>
        void PlayInterceptSound(float3 position);

        /// <summary>
        /// Play impact sound effect (building collapse + explosion).
        /// </summary>
        void PlayImpactSound(float3 position, bool isBallistic);

        /// <summary>
        /// Immediately silence any looping mod audio whose category is now muted.
        /// Called by the settings UI on a mute toggle so playback stops at once,
        /// including while the game is paused.
        /// </summary>
        void ApplyAudioMuteState();
    }
}
