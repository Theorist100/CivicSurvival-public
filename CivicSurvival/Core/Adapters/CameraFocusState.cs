using Unity.Mathematics;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Pure state-holder for pending camera-focus requests. Process-lifetime in
    /// <c>ServiceRegistry</c>, no vanilla refs. The actual camera pivot write
    /// happens in <see cref="CivicSurvival.Core.Systems.CameraFocusApplierSystem"/>
    /// which drains the queue every OnUpdate.
    ///
    /// <see cref="FocusOnPosition(float3)"/> returns <c>true</c> meaning "request
    /// accepted into queue", not "applied this frame" — apply latency is ≤1
    /// rendering frame.
    /// </summary>
    [InfrastructureService]
    public sealed class CameraFocusState
    {
        private readonly object m_Lock = new();
        private float3? m_PendingPosition;
        private float? m_PendingZoom;

        /// <summary>
        /// Queue a focus request. Returns true = "accepted", never reflects whether
        /// the camera actually moved this frame. Apply latency is ≤1 ECS frame —
        /// <see cref="CivicSurvival.Core.Systems.CameraFocusApplierSystem"/> drains
        /// the queue in its own OnUpdate within <c>Rendering</c>; if the
        /// caller runs after the applier this frame, apply happens next frame.
        /// Callers MUST NOT chain logic on "camera is at X now".
        /// </summary>
        public bool FocusOnPosition(float3 position)
        {
            lock (m_Lock)
            {
                m_PendingPosition = position;
                m_PendingZoom = null;
            }
            return true;
        }

        /// <summary>Queue a focus request with zoom. See <see cref="FocusOnPosition(float3)"/> for latency contract.</summary>
        public bool FocusOnPosition(float3 position, float zoom)
        {
            lock (m_Lock)
            {
                m_PendingPosition = position;
                m_PendingZoom = zoom;
            }
            return true;
        }

        internal bool TryConsume(out float3 position, out float? zoom)
        {
            lock (m_Lock)
            {
                if (m_PendingPosition == null)
                {
                    position = default;
                    zoom = null;
                    return false;
                }
                position = m_PendingPosition.Value;
                zoom = m_PendingZoom;
                m_PendingPosition = null;
                m_PendingZoom = null;
                return true;
            }
        }

        /// <summary>
        /// Drop any pending request. Called by the applier system on world teardown
        /// so a request queued in the dying world doesn't fire on the next world.
        /// </summary>
        internal void Clear()
        {
            lock (m_Lock)
            {
                m_PendingPosition = null;
                m_PendingZoom = null;
            }
        }
    }
}
