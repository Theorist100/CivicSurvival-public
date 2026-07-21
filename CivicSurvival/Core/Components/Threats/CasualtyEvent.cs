using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// ECS event: triggered when casualties occur.
    /// Created by ThreatDamageSystem (Threats), consumed by WorldShockSystem (Attention).
    /// </summary>
    public struct CasualtyEvent : IComponentData
    {
        /// <summary>Number of casualties.</summary>
        public int Count;

        /// <summary>Type determines shock multiplier.</summary>
        public CasualtyType Type;

        /// <summary>Location of the event.</summary>
        public float3 Position;
    }
}
