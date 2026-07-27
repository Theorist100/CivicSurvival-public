using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.CameraTracking;
using CivicSurvival.Core.Types.Snapshots;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for camera tracking state.
    /// The live snapshot is owned by the world-bound <see cref="CameraTrackingSystem"/>;
    /// this facade is rebound to the surviving host on mod hot-reload.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class CameraTrackingState
    {
        private CameraTrackingSystem? m_CurrentHost;

        internal CameraTrackingSystem? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        /// <summary>Current immutable snapshot of camera state.</summary>
        public CameraSnapshot Current
        {
            get
            {
                var host = CurrentHost;
                return host != null ? host.GetCurrentSnapshot() : default;
            }
        }
    }
}
