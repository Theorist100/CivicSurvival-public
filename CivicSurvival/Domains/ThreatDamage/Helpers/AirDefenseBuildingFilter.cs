using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Tracks the set of host entities carrying AirDefenseInstallations (the placed AA
    /// objects — StaticObjectPrefab props, not vanilla buildings; "building" in the names
    /// here is historical) and excludes them from random debris-fire victim picks.
    ///
    /// Refresh is event-driven: rebuild when the AA installation query changes.
    /// Count-only invalidation misses same-frame replacement, so the helper also
    /// tracks the query component order version.
    ///
    /// The owning system holds the <see cref="NativeHashSet{Entity}"/> directly so
    /// CIVIC023 can verify Dispose ordering against OnDestroy. This helper never
    /// allocates native memory itself — it operates over the caller-owned set.
    /// </summary>
    internal sealed class AirDefenseBuildingFilter
    {
        private EntityQuery m_Query;
        private int m_LastCount = -1;
        [EntityQueryOrderCursor("Invalidates the AA-building filter when the building query's archetype set changes.")]
        private int m_OrderCursor;

        public void Initialize(EntityQuery aaQuery)
        {
            m_Query = aaQuery;
            m_LastCount = -1;
            m_OrderCursor = 0;
        }

        public bool Contains(in NativeHashSet<Entity> buildings, Entity building)
            => buildings.IsCreated && buildings.Contains(building);

        /// <summary>
        /// Rebuild the AA host-object set if the installation query changed.
        /// Cheap no-op when count and structural version are stable.
        /// </summary>
        public void Refresh(EntityManager em, NativeHashSet<Entity> buildings)
        {
            int currentCount = m_Query.CalculateEntityCountWithoutFiltering();
            int currentOrderVersion = m_Query.GetCombinedComponentOrderVersion(includeEntityType: true);
            if (currentCount == m_LastCount && currentOrderVersion == m_OrderCursor) return;
            m_LastCount = currentCount;
            m_OrderCursor = currentOrderVersion;

            buildings.Clear();

            using (PerformanceProfiler.Measure("SP:ADF.Refresh"))
            {
                var entities = m_Query.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var aa = em.GetComponentData<AirDefenseInstallation>(entities[i]);
                        Entity building = aa.GetBuildingEntity();
                        if (building != Entity.Null)
                            buildings.Add(building);
                    }
                }
                finally
                {
                    if (entities.IsCreated) entities.Dispose();
                }
            }
        }

        /// <summary>
        /// Pick a random building from <paramref name="nearbyBuildings"/> that is
        /// not an AA installation. Returns Entity.Null when nothing eligible.
        /// Compacts the input list in-place (caller already owns and disposes it).
        /// </summary>
        public Entity PickRandomEligibleVictim(
            NativeList<Entity> nearbyBuildings,
            in NativeHashSet<Entity> aaBuildings,
            ref SerializableRandom random)
        {
            int eligibleCount = 0;

            for (int i = 0; i < nearbyBuildings.Length; i++)
            {
                Entity candidate = nearbyBuildings[i];
                if (aaBuildings.IsCreated && aaBuildings.Contains(candidate))
                    continue;

                nearbyBuildings[eligibleCount++] = candidate;
            }

            if (eligibleCount == 0)
                return Entity.Null;

            return nearbyBuildings[random.Next(0, eligibleCount)];
        }

        /// <summary>
        /// Reset the query-change guard so the next Refresh always rebuilds.
        /// Caller is responsible for clearing the buildings set itself.
        /// </summary>
        public void ResetCount()
        {
            m_LastCount = -1;
            m_OrderCursor = 0;
        }
    }
}
