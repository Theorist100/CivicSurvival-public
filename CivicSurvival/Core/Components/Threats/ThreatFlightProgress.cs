using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Runtime-only no-progress watchdog state for live threats.
    /// Movement jobs update the minimum observed distance to target; if that
    /// minimum stops improving long enough, the threat is exhausted and routed
    /// through the normal crashed/debris pipeline.
    ///
    /// Threat entities persist across save/load, but this payload is deliberately
    /// not durable gameplay state. Live restored threats are reset to a fresh
    /// sentinel state by ThreatLoadRenderReinitSystem.
    /// </summary>
    public struct ThreatFlightProgress : IComponentData, IEmptySerializable
    {
        public float MinDistanceToTarget;
        public double MinDistanceTime;

        public void SetDefaults()
        {
            MinDistanceToTarget = float.MaxValue;
            MinDistanceTime = 0.0;
        }
    }
}
