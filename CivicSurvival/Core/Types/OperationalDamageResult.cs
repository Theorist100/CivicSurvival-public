using CivicSurvival.Core.Components.Threats;
using Unity.Entities;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Result of damage calculation for power plants.
    /// Caller is responsible for applying ECB operations.
    /// </summary>
    public struct OperationalDamageResult
    {
        /// <summary>True if entity is a valid power plant.</summary>
        public bool IsValid;

        /// <summary>True if plant exceeded damage threshold and should be destroyed.</summary>
        public bool ShouldDestroy;

        /// <summary>The damage data to apply via ECB.</summary>
        public PowerPlantDamage Damage;

        /// <summary>
        /// Mod entity that holds PowerPlantDamage (real or ECB-deferred).
        /// Used by destruction block to mark Deleted on the correct entity.
        /// </summary>
        public Entity ResolvedModEntity;
    }
}
