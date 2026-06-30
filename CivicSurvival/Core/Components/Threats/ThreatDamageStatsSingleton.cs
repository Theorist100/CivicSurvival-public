using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces;
using Unity.Entities;

using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Global damage statistics as ECS singleton.
    /// Tracks destruction caused by threats.
    ///
    /// Access: SystemAPI.GetSingleton&lt;ThreatDamageStatsSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;ThreatDamageStatsSingleton&gt;()
    ///
    /// Writer: DamageStatsUpdateSystem (Core, updates each frame from entity queries)
    /// Readers: CityStabilitySystem (stability calculation)
    ///
    /// NOTE: Intentionally no ISerializable - stats are recalculated every frame
    /// from entity queries. Data is derived, not persisted.
    /// </summary>
    [CivicSingleton]
    public struct ThreatDamageStatsSingleton : IComponentData, IEmptySerializable
    {
        /// <summary>Number of buildings currently destroyed</summary>
        public int BuildingsDestroyed;

        /// <summary>Number of buildings currently on fire</summary>
        public int BuildingsOnFire;

        public static ThreatDamageStatsSingleton Default => new();

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }

        // IEmptySerializable marker: stats are recalculated every frame from entity
        // queries — no persisted payload.

        public void SetDefaults() { this = Default; }
    }
}
