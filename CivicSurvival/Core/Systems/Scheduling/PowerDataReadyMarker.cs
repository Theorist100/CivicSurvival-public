using Unity.Entities;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system that signals when power grid data is ready for consumption.
    ///
    /// This enables domain decoupling:
    /// - PowerGridDataSystem: RegisterBefore(typeof(PowerDataReadyMarker))]
    /// - Consumer systems: RegisterAfter(typeof(PowerDataReadyMarker))]
    ///
    /// Both reference Core type, not each other's domain types.
    /// </summary>
    public partial class PowerDataReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
