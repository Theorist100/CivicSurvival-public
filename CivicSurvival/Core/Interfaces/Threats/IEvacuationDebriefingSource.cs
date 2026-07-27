using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Threats
{
    /// <summary>
    /// Wave-end evacuation snapshot for the debriefing modal. Implemented by
    /// <c>ThreatEvacuationSystem</c> (Waves domain), consumed by <c>ThreatUISystem</c>
    /// (ThreatUI domain) — Core interface keeps the read Axiom-5 clean.
    ///
    /// Both values are captured ONCE per wave, at the moment the wave ends (all threats
    /// resolved) — the same moment the debriefing numbers themselves are frozen.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.WavesName)]
    [GenerateNullObject]
    public interface IEvacuationDebriefingSource
    {
        /// <summary>Citizens inside a shelter at the moment the wave ended.</summary>
        int LastWaveEvacSheltered { get; }

        /// <summary>
        /// Citizens still running for a shelter at wave end — they are turned around at the
        /// all-clear (grace after wave end), so for the player this reads "did not make it
        /// in time", not "lost".
        /// </summary>
        int LastWaveEvacEnRoute { get; }
    }
}
