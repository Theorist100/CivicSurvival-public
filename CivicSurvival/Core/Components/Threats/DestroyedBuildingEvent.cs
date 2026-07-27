using CivicSurvival.Core.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Ephemeral event entity for building destroyed by threat.
    /// Created by ThreatDamageSystem, consumed by WorldShockReactionSystem.
    ///
    /// IMPORTANT: This is an EVENT ENTITY, not a component on vanilla building!
    /// We NEVER AddComponent to vanilla buildings (causes homeless spike cascade).
    ///
    /// No serialization needed - ephemeral (processed same/next frame).
    /// </summary>
    public struct DestroyedBuildingEvent : IComponentData
    {
        /// <summary>Building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>True if critical infrastructure (power, water, hospital)</summary>
        public bool IsCritical;

        /// <summary>True if this is a power plant (for PP vs civilian destroyed counter split)</summary>
        public bool IsPowerPlant;

        /// <summary>World-space destruction point.</summary>
        public float3 Position;

        /// <summary>Reconstruct building Entity from typed ref.</summary>
        public Entity GetBuildingEntity() => Building.ToEntity();
    }
}
