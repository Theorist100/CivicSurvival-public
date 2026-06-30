using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for vanilla area update collection flags.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class AreaCollectState : IAreaCollectReader
    {
        private VanillaAreasAdapter? m_CurrentHost;

        internal VanillaAreasAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public bool IsReady => CurrentHost?.HasObservedUpdate == true;
        public string? UnavailableReason => CurrentHost?.UnavailableReason;
        public bool DistrictsUpdated => CurrentHost?.DistrictsUpdated == true;
    }
}
