
namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Threat/wave domain DTO.
    /// ThreatTargets, RadarThreats, RadarTargets, MapBounds are pre-serialized JSON.
    /// Debriefing state is captured from WaveEndedEvent, rebuilt in OnPanelUpdate.
    ///
    /// Producer-readiness convention:
    /// • <c>ProducerReady</c> reports whether the wave producer is initialised.
    /// • When false the WAR view returns null. Add a per-DTO ReasonId only when a
    ///   specific localised reason is needed.
    /// </summary>
    public partial struct ThreatDto : IDomainDto
    {
        // Wave state
        public string WavePhase;
        public int WaveNumber;
        public int ThreatsExpected;
        public int ThreatsSpawned;
        public int ThreatsRemaining;
        public int ThreatsIntercepted;
        public int ThreatsHit;
        public int ThreatsCrashed;
        public float TimeInPhase;
        public float PhaseEndTime;
        public bool ScenarioStarted;
        public bool ProducerReady;
        public string WaveDataStatus;
        // Calm expired but the wave is held for the dawn/dusk launch window: the countdown
        // (PhaseEndTime - TimeInPhase) is stale at 0, so UI shows a waiting status instead.
        public bool WaitingForLaunchWindow;

        // Localized strings
        public string EarlyWarningMessage;
        public string IntelReportLabel;
        public string NoActiveThreatsLabel;

        // Pre-serialized JSON
        public string ThreatTargetsJson;
        public string RadarThreatsJson;
        public string RadarTargetsJson;
        public string RadarDefensesJson;
        public string MapBoundsJson;

        // Identify pipeline
        public int IdentifyTrackedEntity;
        public float IdentifyProgress;
        public bool IdentifyConfirmed;
        public bool IdentifyFocusActive;

        // Debriefing
        public bool ShowDebriefing;
        public int DebriefingWave;
        public int DebriefingIntercepted;
        public int DebriefingHits;
        public int DebriefingShotsFired;
        public int DebriefingCasualties;
        public int DebriefingDamageCost;
        public long DebriefingInfraDamageCost;
        public int DebriefingCrashed;
        public int DebriefingTotalThreats;
        public float DebriefingEfficiency;

        // Radar interception flash markers
        public string RadarInterceptionsJson;

        // Camera "you are here" marker — world ground-target (pivot) X/Z of the
        // active camera. When the camera is unavailable these carry
        // CameraMarkerSentinel so the UI normalizes them out of [0,100] and
        // simply doesn't draw the marker (no crash, no false dot at origin).
        public float CameraX;
        public float CameraZ;

        /// <summary>
        /// Far out-of-bounds value meaning "no camera position". Float writer zeroes
        /// NaN/Inf, so a finite sentinel is used; after normalization it lands well
        /// outside [0,100] and the UI skips the marker.
        /// </summary>
        public const float CameraMarkerSentinel = -1_000_000_000f;
    }
}
