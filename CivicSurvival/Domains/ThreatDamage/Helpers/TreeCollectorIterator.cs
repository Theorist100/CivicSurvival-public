using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Objects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Collects wild-tree entities within a radius from CS2's static search tree
    /// (<c>Game.Objects.SearchSystem.GetStaticSearchTree</c>) — used to ignite a forest
    /// where debris from a downed drone lands.
    ///
    /// Wild tree = has <c>Game.Objects.Tree</c> and no <c>Owner</c>, mirroring the vanilla
    /// WildTree gate (<c>FireHazardSystem</c> / <c>Events.InitializeSystem</c>): owned trees
    /// (street trees, lot props) are excluded.
    ///
    /// Run synchronously on the main thread AFTER fencing the borrowed tree
    /// (<c>GetStaticSearchTree(out deps); deps.Complete();</c>) — <c>SearchSystem</c> mutates
    /// it from a worker on static changes, so a concurrent read is a native AV (same hazard
    /// handled by <c>FireControlCoordinator</c>).
    /// </summary>
    public struct TreeCollectorIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
    {
        public Bounds3 SearchBounds;
        public float2 Center;
        public float RadiusSq;

        public ComponentLookup<Tree> TreeLookup;
        public ComponentLookup<Owner> OwnerLookup;

        public NativeList<Entity> Results;

        public static TreeCollectorIterator Create(
            Bounds3 searchBounds,
            float2 center,
            float radiusSq,
            ComponentLookup<Tree> treeLookup,
            ComponentLookup<Owner> ownerLookup,
            NativeList<Entity> results)
        {
            return new TreeCollectorIterator
            {
                SearchBounds = searchBounds,
                Center = center,
                RadiusSq = radiusSq,
                TreeLookup = treeLookup,
                OwnerLookup = ownerLookup,
                Results = results
            };
        }

        public bool Intersect(QuadTreeBoundsXZ bounds)
        {
            return MathUtils.Intersect(bounds.m_Bounds, SearchBounds);
        }

        public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
        {
            if (!MathUtils.Intersect(bounds.m_Bounds, SearchBounds))
                return;

            // Wild trees only — owned/planted trees carry an Owner and are not forest.
            if (!TreeLookup.HasComponent(item) || OwnerLookup.HasComponent(item))
                return;

            // Circle filter on the item's own AABB centre (trees have a small footprint, so
            // the centre is a faithful position without a separate Transform lookup).
            float2 centre = new float2(
                (bounds.m_Bounds.min.x + bounds.m_Bounds.max.x) * 0.5f,
                (bounds.m_Bounds.min.z + bounds.m_Bounds.max.z) * 0.5f);
            if (math.distancesq(centre, Center) > RadiusSq)
                return;

            Results.Add(item);
        }
    }
}
