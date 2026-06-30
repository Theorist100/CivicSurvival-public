using CivicSurvival.Core.Components.Threats;
using Unity.Mathematics;

namespace CivicSurvival.Domains.ThreatFlight.Helpers
{
    public static class ThreatFlightProgressLogic
    {
        public const float NO_PROGRESS_THRESHOLD = 120f;
        public const float NO_PROGRESS_EPS = 5f;

        public static bool UpdateAndCheckStuck(
            ref ThreatFlightProgress progress,
            float currentDistance,
            double now,
            float eps = NO_PROGRESS_EPS,
            double threshold = NO_PROGRESS_THRESHOLD)
        {
            if (!math.isfinite(currentDistance) || !math.isfinite(now))
                return true;

            if (currentDistance < progress.MinDistanceToTarget - eps)
            {
                progress.MinDistanceToTarget = currentDistance;
                progress.MinDistanceTime = now;
                return false;
            }

            return now - progress.MinDistanceTime > threshold;
        }
    }
}
