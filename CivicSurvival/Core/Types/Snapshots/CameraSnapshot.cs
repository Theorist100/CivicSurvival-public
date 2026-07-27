using Unity.Entities;

namespace CivicSurvival.Core.Types.Snapshots
{
    /// <summary>
    /// Immutable snapshot of camera tracking state.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public readonly struct CameraSnapshot
    {
        /// <summary>Entity being tracked. Entity.Null = no tracking.</summary>
        public readonly Entity TrackedEntity;

        /// <summary>
        /// Detach transition progress: 1.0 = just detached, 0.0 = done.
        /// While > 0, CameraControllerPatch gradually releases terrain lerp
        /// so camera doesn't snap to ground after drone tracking.
        /// </summary>
        public readonly float TransitionProgress;

        /// <summary>
        /// True iff <see cref="TrackedEntity"/> currently carries both
        /// <c>Shahed</c> and <c>ActiveThreat</c> components — i.e. the camera is
        /// following a live drone threat. Consumed by <c>MotionBlurHandlerSystem</c>
        /// to suppress motion blur on the airborne viewport (Bezier interpolation
        /// jitter is most visible on fast-moving drones at 2x/3x sim speed).
        /// Detach paths publish with this flag at its default <c>false</c>.
        /// </summary>
        public readonly bool IsDroneTracking;

        public CameraSnapshot(Entity trackedEntity, float transitionProgress = 0f, bool isDroneTracking = false)
        {
            TrackedEntity = trackedEntity;
            TransitionProgress = transitionProgress;
            IsDroneTracking = isDroneTracking;
        }

        /// <summary>No tracking — used as VersionedView seed before first publish.</summary>
        public static CameraSnapshot Empty { get; } = new(Entity.Null);
    }
}
