using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system for ordering consumers after MaintenanceContractSystem publishes contract state.
    /// Producer systems order themselves before this marker.
    /// </summary>
    public partial class MaintenanceContractReadyMarker : SystemBase
    {
        /// <summary>
        /// Shared ThrottlePhaseKey for all systems in the maintenance-contract pipeline.
        /// MCS (producer) and CSUS (consumer) must tick on the same frame.
        /// </summary>
        public const string PHASE_KEY = "MaintenanceContractPipeline";

        protected override void OnUpdate() { }
    }
}
