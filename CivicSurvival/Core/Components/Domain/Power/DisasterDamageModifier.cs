using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Written exclusively by PowerPlantDisasterSystem.
    /// Damage from random failures (0.0 - 1.0, Minor=0.5, Major=1.0).
    /// Present on regular power plant entities (NOT OutsideConnection).
    /// </summary>
    public struct DisasterDamageModifier : IComponentData
    {
        /// <summary>Disaster damage from random failures (0.0 - 1.0).</summary>
        public float DisasterDamagePercent;
    }
}
