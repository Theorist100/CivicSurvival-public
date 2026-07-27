using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Singleton for tracking threat outcome statistics per wave.
    ///
    /// Writer: ThreatArrivalSystem (single writer pattern)
    /// Readers: WaveExecutor (debriefing), ThreatUIPanel (display)
    ///
    /// Reset: ThreatArrivalSystem resets when the observed wave number changes.
    /// </summary>
    public struct ThreatOutcomeStatsSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>
        /// Current wave number (for auto-reset detection).
        /// When writer sees different wave number, counters reset to 0.
        /// </summary>
        public int WaveNumber;

        /// <summary>
        /// Threats that reached their target this wave.
        /// Incremented by ThreatArrivalSystem when distance &lt; threshold.
        /// </summary>
        public int HitsCount;

        /// <summary>
        /// Drone (Shahed) hits this wave — the Shahed slice of <see cref="HitsCount"/>.
        /// Booked at the same terminalization site for developer balance telemetry
        /// (balance.wave_result); DroneHitsCount + BallisticHitsCount == HitsCount.
        /// </summary>
        public int DroneHitsCount;

        /// <summary>
        /// Ballistic hits this wave — the ballistic slice of <see cref="HitsCount"/>.
        /// Booked at the same terminalization site for developer balance telemetry.
        /// </summary>
        public int BallisticHitsCount;

        /// <summary>
        /// Hits this wave that air defense was actually responsible for — the threat was a
        /// candidate of at least one ready AA during its flight (<c>WasCandidate</c>). Subset of
        /// <see cref="HitsCount"/>: a threat that flew outside every operational umbrella is
        /// counted in HitsCount but NOT here.
        ///
        /// This — not HitsCount — is the denominator the wave leak regulator measures its kill
        /// share against. Using the raw hit count would mean an undefended corner of the map
        /// permanently depresses the measured share, which switches the regulator off over the
        /// defended core: the game would then reward leaving coverage gaps and punish covering
        /// the whole city. Counting only covered hits removes that inversion, and makes a
        /// deliberate sacrifice cost the player a threat inside their own umbrella.
        /// </summary>
        public int CoveredHitsCount;

        /// <summary>
        /// Threats that crashed (fuel exhausted / stuck) this wave.
        /// Incremented by ThreatArrivalSystem when threat exceeds max distance.
        /// </summary>
        public int CrashedCount;

        public static ThreatOutcomeStatsSingleton Default => new()
        {
            WaveNumber = 0,
            HitsCount = 0,
            DroneHitsCount = 0,
            BallisticHitsCount = 0,
            CoveredHitsCount = 0,
            CrashedCount = 0
        };

        /// <summary>
        /// Ensure singleton exists in world. Called by ThreatArrivalSystem.OnCreate.
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        // IEmptySerializable marker: per-wave stats are runtime-only, reset by wave
        // lifecycle — no persisted payload.

        public void SetDefaults() { this = Default; }
    }
}
