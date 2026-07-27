using Colossal.Collections;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Iterators
{
    /// <summary>
    /// Iterator for checking line-of-sight between AA and target through buildings.
    /// Uses CS2's StaticSearchTree for efficient spatial queries on static objects.
    ///
    /// Always construct via <see cref="Create"/> — sets MinT and other required defaults.
    /// </summary>
    public struct LineOfSightIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
    {
        private const float SourceAdjacencyExemptionMeters = 8f;

        // Input
        public Line3.Segment Line;
        public Bounds3 SearchBounds;
        public float MinHeight;  // Minimum building height to block (AA can shoot over low buildings)
        public float RaycastEndpointEpsilon;
        // Source AA object (the placed prop) — skip its own AABB so self-occlusion does not block LOS.
        public int SourceBuildingIndex;
        public int SourceBuildingVersion;

        // Lookups
        public ComponentLookup<Building> BuildingLookup;

        // Output
        public bool IsBlocked;
        public Entity BlockingEntity;
        public float3 BlockPoint;
        public float MinT;  // Nearest blocker parameter (0-1 along line)

        /// <summary>
        /// Factory method — ensures MinT = float.MaxValue and all output fields are zeroed.
        /// Replaces fragile manual struct initialisation at every call site.
        /// </summary>
        public static LineOfSightIterator Create(
            Line3.Segment line,
            Bounds3 searchBounds,
            float minHeight,
            float raycastEndpointEpsilon,
            ComponentLookup<Building> buildingLookup,
            int sourceBuildingIndex,
            int sourceBuildingVersion)
        {
            return new LineOfSightIterator
            {
                Line = line,
                SearchBounds = searchBounds,
                MinHeight = minHeight,
                RaycastEndpointEpsilon = raycastEndpointEpsilon,
                SourceBuildingIndex = sourceBuildingIndex,
                SourceBuildingVersion = sourceBuildingVersion,
                BuildingLookup = buildingLookup,
                IsBlocked = false,
                MinT = float.MaxValue,
                BlockingEntity = Entity.Null,
                BlockPoint = float3.zero
            };
        }

        /// <summary>
        /// Check if we should descend into this quadtree node.
        /// </summary>
        public bool Intersect(QuadTreeBoundsXZ bounds)
        {
            return MathUtils.Intersect(bounds.m_Bounds, SearchBounds);
        }

        /// <summary>
        /// Process a found entity — check if it blocks the line of sight.
        /// Tracks nearest blocker (smallest t). Quadtree traversal order is not distance-sorted.
        /// </summary>
        public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
        {
            // S001: skip the AA's own object — segment starts inside its AABB by construction
            // (AA position is the AA object's transform), so it would always self-block at t.x=0.
            if (item.Index == SourceBuildingIndex && item.Version == SourceBuildingVersion)
                return;

            if (!BuildingLookup.HasComponent(item))
                return;

            // NOTE(L13): AABB max.y approximates building height — actual mesh roof geometry is not accessible.
            // Acceptable for LOS purposes; tall thin structures may pass LOS checks at extreme angles.
            float buildingHeight = bounds.m_Bounds.max.y;
            if (buildingHeight < MinHeight)
                return;

            if (!MathUtils.Intersect(bounds.m_Bounds, Line, out float2 t))
                return;

            // AA is inside (or behind) a building — t.x ≤ 0 means the segment starts inside
            // the AABB. Ignore near-source overlap from adjacent lots/platforms; it is a
            // spawn-point artifact, not actual intervening cover.
            if (t.x <= 0f && t.y > RaycastEndpointEpsilon)
            {
                if (Line.a.y > bounds.m_Bounds.max.y + RaycastEndpointEpsilon)
                    return;

                float2 center = new(
                    (bounds.m_Bounds.min.x + bounds.m_Bounds.max.x) * 0.5f,
                    (bounds.m_Bounds.min.z + bounds.m_Bounds.max.z) * 0.5f);
                float2 source = new(Line.a.x, Line.a.z);
                if (math.distancesq(center, source) <= SourceAdjacencyExemptionMeters * SourceAdjacencyExemptionMeters)
                    return;

                if (!IsBlocked || MinT > 0f)
                {
                    IsBlocked = true;
                    MinT = 0f;
                    BlockingEntity = item;
                    BlockPoint = Line.a;
                }
                return;
            }

            // Target is inside (or behind) a building — t.y ≥ 1 means the segment ends inside
            // the AABB. Symmetric case: the threat entity is embedded in the building.
            if (t.y >= 1f - RaycastEndpointEpsilon && t.x > RaycastEndpointEpsilon && t.x < MinT) // L4: consistent epsilon with normal path
            {
                IsBlocked = true;
                MinT = t.x;
                BlockingEntity = item;
                BlockPoint = Line.a + (Line.b - Line.a) * t.x;
                return;
            }

            // Normal case: intersection is along the segment, not at endpoints.
            if (t.x > RaycastEndpointEpsilon && t.x < 1f - RaycastEndpointEpsilon && t.x < MinT)
            {
                IsBlocked = true;
                MinT = t.x;
                BlockingEntity = item;
                BlockPoint = Line.a + (Line.b - Line.a) * t.x;
            }
        }
    }
}
