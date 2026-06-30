using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Game.Simulation;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.ThreatFlight.Helpers;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    /// <summary>
    /// Input data for async Shahed movement job.
    /// </summary>
    public struct ShahedMovementInput
    {
        public float3 CurrentPosition;
        public float3 TargetPosition;
        public float3 SpawnPosition;
        public float Speed;
        public float CurrentDistance;
        public float TotalDistance;
        public BuildingRef TargetBuilding;
        public bool IsAvoiding;
        public float3 AvoidanceWaypoint;
        public BuildingRef AvoidanceObstacle;
        public BuildingRef PreviousAvoidanceObstacle;
        public float AvoidanceCooldown;
        public float TimeSinceCheckpoint;
        public float3 LastCheckpointPos;
        public float MinDistanceToTarget;
        public double MinDistanceTime;
        /// <summary>Current smoothed flight direction (zero = uninitialized).</summary>
        public float3 CurrentDirection;
        /// <summary>Current bank angle in radians (for smoothing).</summary>
        public float CurrentBankAngle;
    }

    /// <summary>
    /// Output data from async Shahed movement job.
    /// Position and distance are DELTAS (not absolute) to prevent jitter
    /// when applied 1-2 frames later to an entity that has since moved.
    /// </summary>
    public struct ShahedMovementOutput
    {
        public float3 PositionDelta;
        public quaternion NewRotation;
        public float3 Velocity;
        public float DistanceDelta;
        public bool IsAvoiding;
        public float3 AvoidanceWaypoint;
        public BuildingRef AvoidanceObstacle;
        public BuildingRef PreviousAvoidanceObstacle;
        public float AvoidanceCooldown;
        public bool AvoidanceCleared;  // True when movement job reached waypoint and cleared avoidance
        public float TimeSinceCheckpoint;
        public float3 LastCheckpointPos;
        public float NewMinDistanceToTarget;
        public double NewMinDistanceTime;
        public bool HasArrived;
        public bool IsExhausted;
        /// <summary>Smoothed direction after this step (persist back to Shahed.CurrentDirection).</summary>
        public float3 NewDirection;
        /// <summary>Smoothed bank angle after this step (persist back to Shahed.BankAngle).</summary>
        public float NewBankAngle;
    }

    /// <summary>
    /// Burst-compiled async movement job for Shahed drones.
    /// Scheduled frame N, results applied frame N+1.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct ShahedMovementJob : Unity.Jobs.IJobParallelFor
    {
        private const float WAYPOINT_REACHED_DIST = 30f;
        private const float AVOIDANCE_COOLDOWN_SECONDS = 1.5f;
        private const float REPLAN_COOLDOWN_SECONDS = 0.35f;
        private const float REPLAN_IMPROVEMENT_MARGIN = 30f;
        private const float CHECKPOINT_INTERVAL_SECONDS = 1.0f;
        private const float MIN_PROGRESS_PER_CHECKPOINT = 15f;
        private const float DIVE_DISTANCE = 300f;
        private const float MIN_CRUISE_ALTITUDE = 100f;
        private const float EXHAUSTION_DISTANCE_RATIO = 0.3f;
        private const float EXHAUSTION_DETOUR_MULTIPLIER = 1.5f;

        /// <summary>
        /// Max heading change per TMS step (every 16 ticks = 0.267s).
        /// 15° per step ≈ 56°/s → 180° turn in ~3.2s → turn radius ~41m at 40 m/s.
        /// Ensures TF slot gap stays ~10.67m even during sharp turns (prevents Bezier backward).
        /// </summary>
        private const float MAX_TURN_RADIANS_PER_STEP = 15f * math.PI / 180f;

        // ===== Bank (roll into turns) =====
        private const float MAX_BANK_RAD = 35f * math.PI / 180f;  // 35° max bank
        private const float BANK_SENSITIVITY = 3.0f;               // yaw rate → bank multiplier
        private const float BANK_SMOOTHING_SPEED = 4.0f;           // exponential smoothing (1/s)

        // ===== Pitch during cruise =====
        private const float CRUISE_PITCH_FACTOR = 0.3f;  // 0=flat, 1=full pitch follow

        // ===== Wing wobble (turbulence) =====
        private const float WOBBLE_AMPLITUDE_RAD = 2.5f * math.PI / 180f;  // ±2.5°
        private const float WOBBLE_FREQ = 1.5f * 2f * math.PI;             // 1.5 Hz in rad/s
        private const float WOBBLE_PHASE_OFFSET = 1.618034f;               // Golden ratio — irrational spacing per entity

        public float DeltaTime;
        public double ElapsedTime;
        public TerrainHeightData TerrainData;

        // Reads the unified collect record (entity + input + radar payload). Movement only
        // needs the .Input view; keeping one array preserves index-parallelism with apply.
        [ReadOnly] public NativeArray<ShahedCollectedInput> Collected;
        [WriteOnly] public NativeArray<ShahedMovementOutput> Outputs;

        [ReadOnly] public NativeList<CachedBuilding> Buildings;
        [ReadOnly] public NativeParallelMultiHashMap<int2, int> BuildingGrid;
        public bool DoObstacleCheck;

        // Diagnostic sink: obstacle avoidance enqueues any building-grid index that overran
        // the building cache (native AV guard). Drained on the main thread next cycle.
        [WriteOnly] public NativeQueue<int>.ParallelWriter ObstacleOobLog;

        public void Execute(int index)
        {
            var input = Collected[index].Input;
            var output = new ShahedMovementOutput();
            output.NewMinDistanceToTarget = input.MinDistanceToTarget;
            output.NewMinDistanceTime = input.MinDistanceTime;

            float3 currentPos = input.CurrentPosition;
            float3 targetPos = input.TargetPosition;
            float3 spawnPos = input.SpawnPosition;

            float terrainHeight = TerrainData.isCreated
                ? TerrainUtils.SampleHeight(ref TerrainData, currentPos)
                : currentPos.y - MIN_CRUISE_ALTITUDE;
            float cruiseAltitude = math.max(terrainHeight + MIN_CRUISE_ALTITUDE, spawnPos.y);
            float distanceXZ = math.distance(currentPos.xz, targetPos.xz);

            float cooldown = input.AvoidanceCooldown;
            if (cooldown > 0f) cooldown = math.max(0f, cooldown - DeltaTime);

            bool isAvoiding = input.IsAvoiding;
            float3 avoidanceWaypoint = input.AvoidanceWaypoint;
            BuildingRef avoidanceObstacle = input.AvoidanceObstacle;
            BuildingRef previousAvoidanceObstacle = input.PreviousAvoidanceObstacle;
            float initialTurnMargin = CalculateTurnCorridorMargin(input.CurrentDirection, targetPos - currentPos);

            // Obstacle avoidance: check for buildings in flight path (throttled by DoObstacleCheck)
#pragma warning disable CIVIC251 // Cooldown countdown — monotonic decrease to 0, no oscillation possible
            if (DoObstacleCheck && !isAvoiding && cooldown <= 0f && distanceXZ > DIVE_DISTANCE)
#pragma warning restore CIVIC251
            {
                if (ObstacleAvoidanceHelper.TryFindObstacleOnSegment(
                    currentPos, targetPos, ObstacleAvoidanceHelper.LOOK_AHEAD_DISTANCE,
                    Buildings, BuildingGrid, ObstacleOobLog,
                    input.TargetBuilding.Index, input.TargetBuilding.Version,
                    -1, -1, -1, -1,
                    initialTurnMargin,
                    out float3 obstCenter, out float obstRadius, out float obstHeight, out float obstDist,
                    out BuildingRef obstBuilding))
                {
                    isAvoiding = true;
                    avoidanceWaypoint = ObstacleAvoidanceHelper.CalculateAvoidanceWaypoint(
                        currentPos, targetPos, obstCenter, obstRadius, obstHeight, obstDist);
                    previousAvoidanceObstacle = default;
                    avoidanceObstacle = obstBuilding;
                    cooldown = REPLAN_COOLDOWN_SECONDS;
                }
            }

            float3 effectiveTarget;
            if (isAvoiding)
            {
                effectiveTarget = avoidanceWaypoint;
                // Use whichever is higher: cruise altitude or waypoint Y (building clearance)
                effectiveTarget.y = math.max(cruiseAltitude, avoidanceWaypoint.y);

                float distToWaypoint = math.distance(currentPos.xz, effectiveTarget.xz);
                if (distToWaypoint < WAYPOINT_REACHED_DIST)
                {
                    isAvoiding = false;
                    avoidanceWaypoint = float3.zero;
                    avoidanceObstacle = default;
                    previousAvoidanceObstacle = default;
                    cooldown = AVOIDANCE_COOLDOWN_SECONDS;
                    output.AvoidanceCleared = true;

                    // Re-acquire the real target immediately after waypoint clear.
                    // Without this, the drone spends one extra step steering toward the
                    // stale avoidance waypoint even though avoidance already ended.
                    if (distanceXZ > DIVE_DISTANCE)
                    {
                        effectiveTarget = new float3(targetPos.x, cruiseAltitude, targetPos.z);
                    }
                    else
                    {
                        float t = math.saturate(1f - (distanceXZ / DIVE_DISTANCE));
#pragma warning disable CIVIC073 // t is math.saturate() on previous line
                        float diveAltitude = math.lerp(cruiseAltitude, targetPos.y, t);
#pragma warning restore CIVIC073
                        effectiveTarget = new float3(targetPos.x, diveAltitude, targetPos.z);
                    }
                }
            }
            else if (distanceXZ > DIVE_DISTANCE)
            {
                effectiveTarget = new float3(targetPos.x, cruiseAltitude, targetPos.z);
            }
            else
            {
                float t = math.saturate(1f - (distanceXZ / DIVE_DISTANCE));
#pragma warning disable CIVIC073 // t is math.saturate() on previous line
                float diveAltitude = math.lerp(cruiseAltitude, targetPos.y, t);
#pragma warning restore CIVIC073
                effectiveTarget = new float3(targetPos.x, diveAltitude, targetPos.z);
            }

            float distToCurrentWaypoint = isAvoiding
                ? math.distance(currentPos.xz, effectiveTarget.xz)
                : 0f;

#pragma warning disable CIVIC251 // Short cooldown gates mid-avoidance replans to prevent A/B obstacle churn
            if (DoObstacleCheck && isAvoiding && cooldown <= 0f && distanceXZ > DIVE_DISTANCE)
#pragma warning restore CIVIC251
            {
                float replanTurnMargin = CalculateTurnCorridorMargin(input.CurrentDirection, effectiveTarget - currentPos);
                if (ObstacleAvoidanceHelper.TryFindObstacleOnSegment(
                    currentPos, effectiveTarget, ObstacleAvoidanceHelper.LOOK_AHEAD_DISTANCE,
                    Buildings, BuildingGrid, ObstacleOobLog,
                    input.TargetBuilding.Index, input.TargetBuilding.Version,
                    avoidanceObstacle.Index, avoidanceObstacle.Version,
                    previousAvoidanceObstacle.Index, previousAvoidanceObstacle.Version,
                    replanTurnMargin,
                    out float3 obstCenter, out float obstRadius, out float obstHeight, out float obstDist,
                    out BuildingRef obstBuilding))
                {
#pragma warning disable CIVIC251 // Replan only if the newly detected obstacle is materially before the current waypoint
                    if (obstDist + REPLAN_IMPROVEMENT_MARGIN < distToCurrentWaypoint)
#pragma warning restore CIVIC251
                    {
                        avoidanceWaypoint = ObstacleAvoidanceHelper.CalculateAvoidanceWaypoint(
                            currentPos, effectiveTarget, obstCenter, obstRadius, obstHeight, obstDist);
                        previousAvoidanceObstacle = avoidanceObstacle;
                        avoidanceObstacle = obstBuilding;
                        effectiveTarget = avoidanceWaypoint;
                        effectiveTarget.y = math.max(cruiseAltitude, avoidanceWaypoint.y);
                        cooldown = REPLAN_COOLDOWN_SECONDS;
                    }
                }
            }

            float3 desiredDir = math.normalizesafe(effectiveTarget - currentPos);

            // Smooth turning: limit angular change per step to prevent Bezier backward jitter.
            // When CurrentDirection is zero (first frame after spawn), use desired direction directly.
            float3 currentDir = input.CurrentDirection;
            float3 direction;
            if (math.lengthsq(currentDir) < 0.5f)
            {
                direction = desiredDir;
            }
            else
            {
                direction = RotateTowards(currentDir, desiredDir, MAX_TURN_RADIANS_PER_STEP);
            }

            output.NewDirection = direction;

            float moveDistance = input.Speed * DeltaTime;
            float3 newPos = currentPos + direction * moveDistance;

            output.PositionDelta = direction * moveDistance;

            // ===== Rotation: pitch + bank + wobble =====

            // Pitch: continuous blend — CRUISE_PITCH_FACTOR during cruise, full pitch during dive
            float pitchFactor = distanceXZ > DIVE_DISTANCE ? CRUISE_PITCH_FACTOR : 1.0f;
            float3 rotDir = math.normalizesafe(new float3(direction.x, direction.y * pitchFactor, direction.z));
            quaternion yawPitch = quaternion.LookRotationSafe(rotDir, math.up());

            // Bank: signed yaw delta (XZ plane only) → bank angle, smoothed exponentially
            // Guard: near-vertical direction has no meaningful yaw → zero bank
            float signedYawDelta = 0f;
            if (math.lengthsq(currentDir.xz) > 0.01f)
            {
                float2 curXZ = math.normalizesafe(currentDir.xz);
                float2 desXZ = math.normalizesafe(desiredDir.xz);
                // 2D cross product = sin(angle), positive = turning right
                signedYawDelta = curXZ.x * desXZ.y - curXZ.y * desXZ.x;
            }
            float targetBank = math.clamp(signedYawDelta * BANK_SENSITIVITY, -MAX_BANK_RAD, MAX_BANK_RAD);
#pragma warning disable CIVIC073 // Exponential smoothing: 1-exp(-k*dt) is always in [0,1)
            float smoothBank = math.lerp(input.CurrentBankAngle, targetBank, 1f - math.exp(-BANK_SMOOTHING_SPEED * DeltaTime));
#pragma warning restore CIVIC073

            // Wobble: per-entity periodic roll oscillation (deterministic from index)
            float wobble = math.sin((float)ElapsedTime * WOBBLE_FREQ + index * WOBBLE_PHASE_OFFSET) * WOBBLE_AMPLITUDE_RAD;

            float totalBank = smoothBank + wobble;
            output.NewBankAngle = smoothBank;

            // Apply bank in local space: yawPitch first, then roll around local Z (forward)
            quaternion roll = quaternion.AxisAngle(math.forward(), totalBank);
            output.NewRotation = math.mul(yawPitch, roll);

            output.Velocity = direction * input.Speed;
            output.DistanceDelta = moveDistance;
            output.IsAvoiding = isAvoiding;
            output.AvoidanceWaypoint = avoidanceWaypoint;
            output.AvoidanceObstacle = avoidanceObstacle;
            output.PreviousAvoidanceObstacle = previousAvoidanceObstacle;
            output.AvoidanceCooldown = cooldown;

            // Checkpoint stuck detection
            float3 lastCheckpoint = input.LastCheckpointPos;
            if (input.TimeSinceCheckpoint <= 0f
                && (input.CurrentDistance <= 0f || math.lengthsq(lastCheckpoint) <= 0f))
            {
                lastCheckpoint = currentPos;
            }

            float timeSince = input.TimeSinceCheckpoint + DeltaTime;
            if (output.AvoidanceCleared)
            {
                // Reset both checkpoint fields against this step's movement result.
                // TMS persists PositionDelta by applying currentPos -> newPos on the
                // next frame, so the next checkpoint must measure from newPos too.
                timeSince = 0f;
                lastCheckpoint = newPos;
            }

            bool isDiving = distanceXZ <= DIVE_DISTANCE && !isAvoiding;

            if (timeSince >= CHECKPOINT_INTERVAL_SECONDS)
            {
                float progress = math.distance(lastCheckpoint, targetPos) - math.distance(newPos, targetPos);
                if (progress < MIN_PROGRESS_PER_CHECKPOINT && !isAvoiding && !isDiving)
                {
                    output.IsExhausted = true;
                    output.TimeSinceCheckpoint = timeSince;
                    output.LastCheckpointPos = lastCheckpoint;
                    Outputs[index] = output;
                    return;
                }
                lastCheckpoint = newPos;
                timeSince = 0f;
            }

            output.TimeSinceCheckpoint = timeSince;
            output.LastCheckpointPos = lastCheckpoint;

            // Arrival: if remaining distance <= step, snap to target (prevents VFX/render desync
            // from overshoot or turn smoothing on the final movement step).
            // Gate on !isAvoiding: drone near target may enter avoidance — don't trigger arrival
            // while navigating around obstacle. Arrival fires after avoidance clears.
            float remaining = math.distance(currentPos, targetPos);

            var progressSnap = new ThreatFlightProgress
            {
                MinDistanceToTarget = input.MinDistanceToTarget,
                MinDistanceTime = input.MinDistanceTime
            };

            if (!isAvoiding && !isDiving)
            {
                if (ThreatFlightProgressLogic.UpdateAndCheckStuck(ref progressSnap, remaining, ElapsedTime))
                {
                    output.IsExhausted = true;
                }
            }
            else
            {
                // Avoidance and dive can legitimately hold or increase direct
                // 3D remaining distance. Pause the watchdog window during those
                // manoeuvres while still accepting a genuine new minimum.
                if (remaining < progressSnap.MinDistanceToTarget - ThreatFlightProgressLogic.NO_PROGRESS_EPS)
                    progressSnap.MinDistanceToTarget = remaining;
                progressSnap.MinDistanceTime = ElapsedTime;
            }

            output.NewMinDistanceToTarget = progressSnap.MinDistanceToTarget;
            output.NewMinDistanceTime = progressSnap.MinDistanceTime;

            if (remaining <= moveDistance && !isAvoiding)
            {
                float3 arrivalDelta = targetPos - currentPos;
                output.PositionDelta = arrivalDelta;
                output.Velocity = float3.zero;
                output.DistanceDelta = math.length(arrivalDelta);
                output.NewDirection = math.normalizesafe(arrivalDelta, direction);
                output.NewRotation = quaternion.LookRotationSafe(output.NewDirection, math.up());
                output.HasArrived = true;
                Outputs[index] = output;
                return;
            }

            // Exhaustion check (use accumulated distance, not delta)
            // Skip exhaustion during dive phase — drone is XZ-close and descending to target.
            // Without this, avoidance detour inflates currentDistance past threshold while drone
            // is actively diving, causing false exhaustion instead of arrival.
            float newCurrentDistance = input.CurrentDistance + moveDistance;
            float totalDist = input.TotalDistance;
            if (!isDiving &&
                (newCurrentDistance > totalDist * 2f ||
                (newCurrentDistance > totalDist * EXHAUSTION_DETOUR_MULTIPLIER && remaining > totalDist * EXHAUSTION_DISTANCE_RATIO)))
            {
                output.IsExhausted = true;
            }

            Outputs[index] = output;
        }

        /// <summary>
        /// Rotates normalized vector <paramref name="from"/> toward <paramref name="to"/>
        /// by at most <paramref name="maxRadians"/>. Both inputs must be normalized.
        /// </summary>
        private static float3 RotateTowards(float3 from, float3 to, float maxRadians)
        {
            float dot = math.clamp(math.dot(from, to), -1f, 1f);
            const float epsilon = 0.0001f;
            const float antiparallelThreshold = -0.9999f;

            // Antiparallel guard: sin(π) = 0 → Slerp division by zero.
            // Use perpendicular rotation in XZ plane instead.
            if (dot < antiparallelThreshold)
            {
                float perpX = -from.z;
                float perpZ = from.x;
                float3 rawPerp = new float3(perpX, 0f, perpZ);
                float3 perp = math.lengthsq(rawPerp) < epsilon
                    ? new float3(1f, 0f, 0f)
                    : math.normalizesafe(rawPerp);
                return math.normalizesafe(from + perp * math.sin(maxRadians));
            }

            float angle = math.acos(dot);
            if (angle <= maxRadians || angle < epsilon)
                return to;
#pragma warning disable CIVIC073 // angle > maxRadians guard above guarantees t < 1
            float t = maxRadians / math.max(angle, epsilon);
#pragma warning disable CIVIC021 // sinAngle > 0 guaranteed: dot > -0.9999 + angle > epsilon guards above
            float sinAngle = math.sin(angle);
            float3 result = (math.sin((1f - t) * angle) * from + math.sin(t * angle) * to) / sinAngle;
#pragma warning restore CIVIC021
            return math.normalizesafe(result);
#pragma warning restore CIVIC073
        }

        private static float CalculateTurnCorridorMargin(float3 currentDir, float3 desiredVector)
        {
            if (math.lengthsq(currentDir.xz) < 0.01f || math.lengthsq(desiredVector.xz) < 0.01f)
                return 0f;

            float2 currentXZ = math.normalizesafe(currentDir.xz);
            float2 desiredXZ = math.normalizesafe(desiredVector.xz);
            float dot = math.clamp(math.dot(currentXZ, desiredXZ), -1f, 1f);
            return ObstacleAvoidanceHelper.TURN_CORRIDOR_MARGIN * math.saturate((1f - dot) * 2f);
        }
    }
}
