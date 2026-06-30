using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.ThreatFlight.Jobs;

namespace CivicSurvival.Domains.ThreatFlight.Helpers
{
    /// <summary>
    /// Static helper for obstacle avoidance geometry calculations.
    /// Pure math functions - no ECS dependencies.
    /// </summary>
    public static class ObstacleAvoidanceHelper
    {
        // Spatial grid
        public const float GRID_CELL_SIZE = 150f;

        // Avoidance parameters
        // Drone render position can lag up to ~20m from ECS position
        public const float LATERAL_MARGIN = 60f;  // Detection margin beyond building edge
        public const float AVOIDANCE_MARGIN = 70f;  // Waypoint offset beyond building edge (must be > LATERAL_MARGIN)
        public const float LOOK_AHEAD_DISTANCE = 500f;
        public const float TURN_CORRIDOR_MARGIN = 180f;
        public const float GRID_EXPANSION_MARGIN = LATERAL_MARGIN + TURN_CORRIDOR_MARGIN;
        public const float MIN_ALTITUDE_MARGIN = 50f;
        public const float FORWARD_OFFSET = 100f;  // Waypoint forward offset beyond building edge
        public const float MAX_CLIMB_ANGLE_TAN = 0.268f;  // tan(15°) — max realistic climb for a winged drone
        private const float VERTICAL_AVOIDANCE_SHARE = 0.60f;
        private const float MIN_VISIBLE_CLIMB = 20f;
        private const float VERTICAL_HASH_BUCKETS = 65536f;
        private const float DDA_AXIS_EPSILON = 0.0001f;
        private const float MIN_SEGMENT_LENGTH = 0.001f;
        private const int MAX_DDA_STEPS = 32;

        /// <summary>
        /// Get spatial grid cell for a position.
        /// </summary>
        public static int2 GetGridCell(float3 pos)
        {
            return new int2(
                (int)math.floor(pos.x / GRID_CELL_SIZE),
                (int)math.floor(pos.z / GRID_CELL_SIZE)
            );
        }

        /// <summary>
        /// Find closest obstacle along a bounded XZ segment using DDA grid traversal.
        /// The building cache stores each obstacle in every expanded cell it can touch,
        /// so runtime only checks cells crossed by the segment.
        /// </summary>
        public static bool TryFindObstacleOnSegment(
            float3 currentPos,
            float3 targetPos,
            float maxDistance,
            NativeList<CachedBuilding> buildings,
            NativeParallelMultiHashMap<int2, int> buildingGrid,
            NativeQueue<int>.ParallelWriter oobLog,
            int targetBuildingIndex,
            int targetBuildingVersion,
            int ignoredBuildingIndex,
            int ignoredBuildingVersion,
            int secondIgnoredBuildingIndex,
            int secondIgnoredBuildingVersion,
            float extraLateralMargin,
            out float3 obstacleCenter,
            out float obstacleRadius,
            out float obstacleHeight,
            out float obstacleDistance,
            out BuildingRef obstacleBuilding)
        {
            obstacleCenter = float3.zero;
            obstacleRadius = 0f;
            obstacleHeight = 0f;
            obstacleDistance = 0f;
            obstacleBuilding = default;

            float2 start = currentPos.xz;
            float2 rawSegment = targetPos.xz - start;
            float segmentLength = math.length(rawSegment);
            if (segmentLength < MIN_SEGMENT_LENGTH || maxDistance <= 0f)
                return false;

            float lookAhead = math.min(maxDistance, segmentLength);
            float2 flightDir2D = rawSegment / segmentLength;
            float2 end = start + flightDir2D * lookAhead;

            int2 currentCell = GetGridCell(new float3(start.x, currentPos.y, start.y));
            int2 endCell = GetGridCell(new float3(end.x, currentPos.y, end.y));
            int stepX = flightDir2D.x >= 0f ? 1 : -1;
            int stepZ = flightDir2D.y >= 0f ? 1 : -1;
            float absDirX = math.abs(flightDir2D.x);
            float absDirZ = math.abs(flightDir2D.y);
            bool hasInitialXStep = currentCell.x != endCell.x;
            bool hasInitialZStep = currentCell.y != endCell.y;
            float invDx = absDirX > 0f ? 1f / absDirX : float.MaxValue;
            float invDz = absDirZ > 0f ? 1f / absDirZ : float.MaxValue;
            float nextBoundaryX = stepX > 0
                ? (currentCell.x + 1) * GRID_CELL_SIZE
                : currentCell.x * GRID_CELL_SIZE;
            float nextBoundaryZ = stepZ > 0
                ? (currentCell.y + 1) * GRID_CELL_SIZE
                : currentCell.y * GRID_CELL_SIZE;
            float tMaxX = float.MaxValue;
            float tMaxZ = float.MaxValue;
            if (hasInitialXStep)
                tMaxX = math.max(0f, (nextBoundaryX - start.x) * stepX * invDx);
            if (hasInitialZStep)
                tMaxZ = math.max(0f, (nextBoundaryZ - start.y) * stepZ * invDz);
            float tDeltaX = GRID_CELL_SIZE * invDx;
            float tDeltaZ = GRID_CELL_SIZE * invDz;

            bool foundObstacle = false;
            float closestDist = float.MaxValue;
            var checkedBuildings = new FixedList128Bytes<BuildingRef>();

            for (int step = 0; step < MAX_DDA_STEPS; step++)
            {
                CheckCell(
                    currentCell, currentPos, flightDir2D, lookAhead, extraLateralMargin,
                    buildings, buildingGrid, oobLog,
                    targetBuildingIndex, targetBuildingVersion,
                    ignoredBuildingIndex, ignoredBuildingVersion,
                    secondIgnoredBuildingIndex, secondIgnoredBuildingVersion,
                    ref checkedBuildings,
                    ref foundObstacle, ref closestDist,
                    ref obstacleCenter, ref obstacleRadius, ref obstacleHeight, ref obstacleDistance,
                    ref obstacleBuilding);

                if (currentCell.x == endCell.x && currentCell.y == endCell.y)
                    break;

                bool canStepX = currentCell.x != endCell.x;
                bool canStepZ = currentCell.y != endCell.y;

                if (canStepX && canStepZ && math.abs(tMaxX - tMaxZ) <= DDA_AXIS_EPSILON)
                {
                    CheckCell(
                        new int2(currentCell.x + stepX, currentCell.y),
                        currentPos, flightDir2D, lookAhead, extraLateralMargin,
                        buildings, buildingGrid, oobLog,
                        targetBuildingIndex, targetBuildingVersion,
                        ignoredBuildingIndex, ignoredBuildingVersion,
                        secondIgnoredBuildingIndex, secondIgnoredBuildingVersion,
                        ref checkedBuildings,
                        ref foundObstacle, ref closestDist,
                        ref obstacleCenter, ref obstacleRadius, ref obstacleHeight, ref obstacleDistance,
                        ref obstacleBuilding);
                    CheckCell(
                        new int2(currentCell.x, currentCell.y + stepZ),
                        currentPos, flightDir2D, lookAhead, extraLateralMargin,
                        buildings, buildingGrid, oobLog,
                        targetBuildingIndex, targetBuildingVersion,
                        ignoredBuildingIndex, ignoredBuildingVersion,
                        secondIgnoredBuildingIndex, secondIgnoredBuildingVersion,
                        ref checkedBuildings,
                        ref foundObstacle, ref closestDist,
                        ref obstacleCenter, ref obstacleRadius, ref obstacleHeight, ref obstacleDistance,
                        ref obstacleBuilding);
                    currentCell.x += stepX;
                    currentCell.y += stepZ;
                    tMaxX += tDeltaX;
                    tMaxZ += tDeltaZ;
                }
                else if (canStepX && (!canStepZ || tMaxX < tMaxZ))
                {
                    currentCell.x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (canStepZ)
                {
                    currentCell.y += stepZ;
                    tMaxZ += tDeltaZ;
                }
                else
                {
                    break;
                }
            }

            return foundObstacle;
        }

        private static void CheckCell(
            int2 cell,
            float3 currentPos,
            float2 flightDir2D,
            float lookAhead,
            float extraLateralMargin,
            NativeList<CachedBuilding> buildings,
            NativeParallelMultiHashMap<int2, int> buildingGrid,
            NativeQueue<int>.ParallelWriter oobLog,
            int targetBuildingIndex,
            int targetBuildingVersion,
            int ignoredBuildingIndex,
            int ignoredBuildingVersion,
            int secondIgnoredBuildingIndex,
            int secondIgnoredBuildingVersion,
            ref FixedList128Bytes<BuildingRef> checkedBuildings,
            ref bool foundObstacle,
            ref float closestDist,
            ref float3 obstacleCenter,
            ref float obstacleRadius,
            ref float obstacleHeight,
            ref float obstacleDistance,
            ref BuildingRef obstacleBuilding)
        {
            if (!buildingGrid.TryGetFirstValue(cell, out int buildingIdx, out var iterator))
                return;

            do
            {
                // Native-safety guard: buildingIdx is sourced from buildingGrid, a separate
                // container from buildings. In a Burst worker (bounds checks stripped) an index
                // past buildings.Length is a raw out-of-bounds read → process-killing AV. The
                // producer (CacheBuildingsChunkJob) writes indices in lockstep with the cache, so
                // an OOB here means a grid/cache desync we have not yet root-caused — record the
                // index for telemetry and skip this entry instead of crashing.
                if ((uint)buildingIdx >= (uint)buildings.Length)
                {
                    oobLog.Enqueue(buildingIdx);
                    continue;
                }

                CheckBuilding(
                    buildings[buildingIdx], currentPos, flightDir2D, lookAhead, extraLateralMargin,
                    targetBuildingIndex, targetBuildingVersion,
                    ignoredBuildingIndex, ignoredBuildingVersion,
                    secondIgnoredBuildingIndex, secondIgnoredBuildingVersion,
                    ref checkedBuildings,
                    ref foundObstacle, ref closestDist,
                    ref obstacleCenter, ref obstacleRadius, ref obstacleHeight, ref obstacleDistance,
                    ref obstacleBuilding);
            } while (buildingGrid.TryGetNextValue(out buildingIdx, ref iterator));
        }

        private static void CheckBuilding(
            CachedBuilding building,
            float3 currentPos,
            float2 flightDir2D,
            float lookAhead,
            float extraLateralMargin,
            int targetBuildingIndex,
            int targetBuildingVersion,
            int ignoredBuildingIndex,
            int ignoredBuildingVersion,
            int secondIgnoredBuildingIndex,
            int secondIgnoredBuildingVersion,
            ref FixedList128Bytes<BuildingRef> checkedBuildings,
            ref bool foundObstacle,
            ref float closestDist,
            ref float3 obstacleCenter,
            ref float obstacleRadius,
            ref float obstacleHeight,
            ref float obstacleDistance,
            ref BuildingRef obstacleBuilding)
        {
            if (building.Building.Index == targetBuildingIndex &&
                building.Building.Version == targetBuildingVersion)
                return;
            if (building.Building.Index == ignoredBuildingIndex &&
                building.Building.Version == ignoredBuildingVersion)
                return;
            if (building.Building.Index == secondIgnoredBuildingIndex &&
                building.Building.Version == secondIgnoredBuildingVersion)
                return;
            if (WasAlreadyChecked(building.Building, ref checkedBuildings))
                return;

            float droneAltitude = currentPos.y;
            if (building.Height < droneAltitude - MIN_ALTITUDE_MARGIN)
                return;

            float expandedRadius = building.Radius + LATERAL_MARGIN + math.max(extraLateralMargin, 0f);
            float expandedRadiusSq = expandedRadius * expandedRadius;
            float2 toBuilding = building.Center.xz - currentPos.xz;
            float projectedDist = math.dot(toBuilding, flightDir2D);
            if (projectedDist < 10f)
                return;
            float2 perpendicular = toBuilding - flightDir2D * projectedDist;
            float lateralSq = math.lengthsq(perpendicular);
            if (lateralSq > expandedRadiusSq)
                return;

            // Bounded segment test: clamp projection to [0, lookAhead] and compare distance to clamped point.
            // Without this, a building behind start or past lookAhead with center near the infinite ray
            // would pass the perpendicular check.
            float clampedDist = math.clamp(projectedDist, 0f, lookAhead);
            float2 closestOnSegment = currentPos.xz + flightDir2D * clampedDist;
#pragma warning disable CIVIC078 // Non-constant threshold (expandedRadius) needs actual distance
            if (math.distancesq(closestOnSegment, building.Center.xz) > expandedRadiusSq)
                return;
#pragma warning restore CIVIC078

            float halfChord = math.sqrt(expandedRadiusSq - lateralSq);
            float entryDist = projectedDist - halfChord;
            float exitDist = projectedDist + halfChord;
            if (exitDist < 10f || entryDist > lookAhead)
                return;

            float obstacleEntryDistance = math.max(0f, entryDist);
            if (obstacleEntryDistance < closestDist)
            {
                foundObstacle = true;
                closestDist = obstacleEntryDistance;
                obstacleCenter = building.Center;
                obstacleRadius = building.Radius;
                obstacleHeight = building.Height;
                obstacleDistance = obstacleEntryDistance;
                obstacleBuilding = building.Building;
            }
        }

        private static bool WasAlreadyChecked(BuildingRef building, ref FixedList128Bytes<BuildingRef> checkedBuildings)
        {
            for (int i = 0; i < checkedBuildings.Length; i++)
            {
                if (checkedBuildings[i] == building)
                    return true;
            }

            if (checkedBuildings.Length < checkedBuildings.Capacity)
                checkedBuildings.Add(building);
            return false;
        }

        /// <summary>
        /// Calculate avoidance waypoint to go around an obstacle.
        /// </summary>
        public static float3 CalculateAvoidanceWaypoint(
            float3 currentPos,
            float3 targetPos,
            float3 obstacleCenter,
            float obstacleRadius,
            float obstacleHeight,
            float obstacleDistance)
        {
            float2 flightDir2D = math.normalizesafe(targetPos.xz - currentPos.xz);

#pragma warning disable S2234 // Intentional swap: perpendicular vector = (-y, x)
            float2 perpDir = new float2(-flightDir2D.y, flightDir2D.x);
#pragma warning restore S2234

            // Choose side that leads toward target
            float2 obstacleToTarget = targetPos.xz - obstacleCenter.xz;
            float dotRight = math.dot(obstacleToTarget, perpDir);
            float2 avoidDir = dotRight > 0 ? perpDir : -perpDir;

            float2 waypointXZ = obstacleCenter.xz
                + avoidDir * (obstacleRadius + AVOIDANCE_MARGIN)
                + flightDir2D * (obstacleRadius + FORWARD_OFFSET);

            // Vertical clearance is deterministic per encounter: most cases climb,
            // the rest stay lateral-only so movement does not become one-note.
            float heightNeeded = obstacleHeight + MIN_ALTITUDE_MARGIN - currentPos.y;
            float waypointY = currentPos.y;
            if (ShouldPreferVerticalAvoidance(currentPos, obstacleCenter) && heightNeeded > 0f && obstacleDistance > 0f)
            {
                float climbPlanningDistance = math.max(
                    obstacleDistance,
                    math.distance(currentPos.xz, obstacleCenter.xz));
                float maxClimb = math.max(climbPlanningDistance, 1f) * MAX_CLIMB_ANGLE_TAN;
                float feasibleY = math.min(obstacleHeight + MIN_ALTITUDE_MARGIN, currentPos.y + maxClimb);
                if (feasibleY - currentPos.y >= MIN_VISIBLE_CLIMB)
                    waypointY = feasibleY;
            }

            return new float3(waypointXZ.x, waypointY, waypointXZ.y);
        }

        private static bool ShouldPreferVerticalAvoidance(float3 currentPos, float3 obstacleCenter)
        {
            int4 encounterKey = new int4(
                (int)math.floor(obstacleCenter.x),
                (int)math.floor(obstacleCenter.z),
                (int)math.floor(currentPos.x / GRID_CELL_SIZE),
                (int)math.floor(currentPos.z / GRID_CELL_SIZE));
            uint hash = math.hash(encounterKey);
            return (hash & 0xFFFFu) < (uint)(VERTICAL_HASH_BUCKETS * VERTICAL_AVOIDANCE_SHARE);
        }
    }
}
