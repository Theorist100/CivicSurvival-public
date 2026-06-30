using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Domains.ThreatFlight.Helpers;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    /// <summary>
    /// Burst-compiled IJobEntity for Ballistic missile movement + arrival detection.
    /// Sets IsArrived=true when remaining distance falls below step size (prevents overshoot).
    /// TAS reads IsArrived as RefRO — zero sync point.
    ///
    /// FIX: Uses ThreatPosition instead of Game.Objects.Transform to avoid
    /// CS2 internal Jobs trying to process our entities as game objects.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct BallisticMovementJobEntity : IJobEntity
    {
        public float DeltaTime;
        public double ElapsedTime;

        // Lofted ballistic arc shape, expressed as fractions of the ground path so the launch
        // and terminal-dive angles are identical on any map size. Climb to cruise over the
        // first slice, hold high across the middle, then dive near-vertically over the last.
        private const float CRUISE_HEIGHT = 3000f;   // arc apex above the higher of launch/target
        private const float CLIMB_FRACTION = 0.12f;  // climb to cruise over the first 12% of the path
        private const float DIVE_FRACTION = 0.12f;   // steep terminal dive over the last 12%

        // ActiveThreat is required for query filter (only process active threats)
#pragma warning disable S1172 // 'active' param unused — required by ECS IJobEntity query filter
        void Execute(
            Entity entity,
            ref Ballistic ballistic,
            in BallisticInterceptState interceptState,
            ref ThreatPosition threatPos,
            ref ThreatFlightProgress progress,
            in ActiveThreat active)
#pragma warning restore S1172
        {
            // Coast gate (Burst): an awaiting (Patriot-intercepted) ballistic keeps flying until its
            // interceptor arrives. AwaitingInterceptorImpact rides the BallisticInterceptState already
            // passed in (interceptState) → no new lookup, no new sync (Axiom 15).
            if ((interceptState.IsIntercepted && !interceptState.AwaitingInterceptorImpact)
                || ballistic.IsArrived)
                return;

            // W7-H1/H2 FIX: Guard NaN/zero/Infinity speed and TargetPosition
            // Infinity TargetPosition → normalizesafe returns valid direction but distance=Inf,
            // so exhaustion check (distFromSpawn > Inf*2) never fires → immortal entity.
            if (ballistic.Speed <= 0f || math.isnan(ballistic.Speed) || math.isinf(ballistic.Speed) ||
                math.any(math.isnan(ballistic.TargetPosition)) || math.any(math.isinf(ballistic.TargetPosition)))
            {
                ballistic.SuppressExhaustedImpact = true;
                ballistic.IsExhausted = true;
                ballistic.IsArrived = true;
                return;
            }

            float3 currentPos = threatPos.Position;
            float3 spawnPos = ballistic.SpawnPosition;
            float3 targetPos = ballistic.TargetPosition;

            // Navigate horizontally toward the target at constant ground speed; the lofted
            // profile below drives altitude. Progress/arrival use the horizontal distance,
            // which shrinks monotonically — the 3D distance temporarily grows during the climb
            // (rising away from a ground target), which would trip a false "stuck" watchdog.
            float2 toTargetXZ = targetPos.xz - currentPos.xz;
            float horizRemaining = math.length(toTargetXZ);
            float horizStep = ballistic.Speed * DeltaTime;

            // Stuck watchdog on the monotonically-shrinking horizontal distance.
            if (ThreatFlightProgressLogic.UpdateAndCheckStuck(ref progress, horizRemaining, ElapsedTime))
            {
                ballistic.IsExhausted = true;
                ballistic.IsArrived = true;
                return;
            }

            // Arrival: within one step of the target on the ground → snap onto the impact point.
            if (horizRemaining <= horizStep)
            {
                float3 hitDir = math.normalizesafe(targetPos - currentPos, new float3(0f, -1f, 0f));
                threatPos.Position = targetPos;
                threatPos.Velocity = float3.zero;
                threatPos.Rotation = quaternion.LookRotationSafe(hitDir, math.up());
                ballistic.IsArrived = true;
                return;
            }

            // Advance horizontally toward the target. normalizesafe avoids the divide-by-zero
            // the arrival early-return already rules out (horizRemaining > horizStep > 0 here).
            float2 horizDir = math.normalizesafe(toTargetXZ);
            float2 newXZ = currentPos.xz + horizDir * horizStep;

            // Ground-path progress 0..1 from spawn to target.
            float totalHoriz = math.distance(spawnPos.xz, targetPos.xz);
            float t = totalHoriz > 1f ? math.saturate(math.distance(newXZ, spawnPos.xz) / totalHoriz) : 1f;

            // Lofted altitude profile: climb to cruise → hold high → steep terminal dive.
            float cruiseY = math.max(spawnPos.y, targetPos.y) + CRUISE_HEIGHT;
            float profileY;
#pragma warning disable CIVIC073 // math.smoothstep output is always in [0,1] — lerp t is in range
            if (t < CLIMB_FRACTION)
                profileY = math.lerp(spawnPos.y, cruiseY, math.smoothstep(0f, CLIMB_FRACTION, t));
            else if (t < 1f - DIVE_FRACTION)
                profileY = cruiseY;
            else
                profileY = math.lerp(cruiseY, targetPos.y, math.smoothstep(1f - DIVE_FRACTION, 1f, t));
#pragma warning restore CIVIC073

            float3 newPos = new float3(newXZ.x, profileY, newXZ.y);
            float3 stepVec = newPos - currentPos;
            // Heading follows the actual arc step, so the missile pitches up on the climb and
            // points down through the dive. Velocity is the real per-tick motion (the terminal
            // dive is genuinely faster) so DroneRenderWriteJob's Moving/TransformFrame matches.
            float3 heading = math.normalizesafe(stepVec, new float3(horizDir.x, 0f, horizDir.y));
            threatPos.Position = newPos;
#pragma warning disable CIVIC021 // DeltaTime is the fixed sim timestep (4/15s, FIXED_TIME_STEP), never zero
            threatPos.Velocity = stepVec / DeltaTime;
#pragma warning restore CIVIC021
            threatPos.Rotation = quaternion.LookRotationSafe(heading, math.up());
        }
    }
}
