using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Types.Snapshots;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for vanilla climate state.
    /// The live snapshot is owned by the world-bound <see cref="VanillaClimateAdapter"/>;
    /// this facade is rebound to the surviving host on mod hot-reload.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class ClimateState
    {
        private VanillaClimateAdapter? m_CurrentHost;

        internal VanillaClimateAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        /// <summary>Current immutable snapshot of climate state.</summary>
        public ClimateSnapshot Current
        {
            get
            {
                var host = CurrentHost;
                return host != null ? host.GetCurrentSnapshot() : ClimateSnapshot.Default;
            }
        }
    }
}
