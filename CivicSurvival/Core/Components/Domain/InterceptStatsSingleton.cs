using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;

using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
namespace CivicSurvival.Core.Components.Domain
{
    /// <summary>
    /// Singleton for tracking intercept statistics per wave.
    /// Updated by InterceptProcessingSystem, read by WaveExecutor.
    ///
    /// Single source of truth for intercept counting.
    /// Avoids race conditions from multiple consumers processing InterceptRequest.
    /// </summary>
    public struct InterceptStatsSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>
        /// Total intercepts this wave (Shahed + Ballistic).
        /// Reset to 0 by InterceptProcessingSystem.ResetForNewWave when WaveExecutor
        /// starts a new wave after debriefing reads the previous wave's value.
        /// </summary>
        public int InterceptedCount;

        /// <summary>
        /// Drone (Shahed) intercepts this wave — the Shahed slice of <see cref="InterceptedCount"/>.
        /// Counted at the same decision site for developer balance telemetry (balance.wave_result);
        /// InterceptedShahedCount + InterceptedBallisticCount == InterceptedCount.
        /// Reset together with the others by ResetForNewWave.
        /// </summary>
        public int InterceptedShahedCount;

        /// <summary>
        /// Ballistic intercepts this wave — the ballistic slice of <see cref="InterceptedCount"/>.
        /// Counted at the same decision site for developer balance telemetry (balance.wave_result).
        /// Reset together with the others by ResetForNewWave.
        /// </summary>
        public int InterceptedBallisticCount;

        /// <summary>
        /// Threats let through this wave by the per-wave leak floor (denied intercepts).
        /// Together with InterceptedCount it drives even leak distribution across the wave.
        /// Reset to 0 by InterceptProcessingSystem.ResetForNewWave.
        /// </summary>
        public int LeakedCount;

        public static InterceptStatsSingleton Default => new()
        {
            InterceptedCount = 0,
            InterceptedShahedCount = 0,
            InterceptedBallisticCount = 0,
            LeakedCount = 0
        };

        public static Entity EnsureExists(EntityManager em)
        {
            return CivicSingleton.Ensure(em, Default);
        }

        // IEmptySerializable marker: stats reset on new wave start by
        // InterceptProcessingSystem — no persisted payload.

        // Required by the serialization guard contract if Deserialize becomes non-empty later.
        // Active wave identity lives in WaveStateSingleton; this component only stores the count.
        public void SetDefaults() { this = Default; }
    }
}
