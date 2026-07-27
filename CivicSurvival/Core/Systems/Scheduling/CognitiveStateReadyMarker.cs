using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that cognitive state data is ready for consumption.
    ///
    /// Purpose: Decouples domain scheduling dependencies.
    /// - CognitiveStateSystem RegisterBefore(CognitiveStateReadyMarker)] guarantees state is written
    /// - This marker also runs AFTER PsyPressureWriterGroup (pressure writers complete first)
    /// - Consumer systems run RegisterAfter(typeof(CognitiveStateReadyMarker))]
    ///
    /// Consumers: ExodusSystem, DonorConferenceSystem, SpotterSpawnSystem
    /// </summary>
    public partial class CognitiveStateReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
