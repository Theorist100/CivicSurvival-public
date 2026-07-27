using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for the static city coastline contour. The live host
    /// (<see cref="VanillaMapContourAdapter"/>) computes the contour once per loaded
    /// city and publishes the JSON here; UI readers pull it through
    /// <see cref="IMapContourReader"/>. Mirrors the MapBoundsState facade/host split
    /// so it reconnects correctly after mod hot-reload.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class MapContourState : IMapContourReader
    {
        private VanillaMapContourAdapter? m_CurrentHost;

        internal VanillaMapContourAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public bool IsReady => CurrentHost?.HasContour == true;
        public string? UnavailableReason => CurrentHost?.UnavailableReason;

        public bool TryGetContourJson(out string contourJson)
        {
            var host = CurrentHost;
            if (host == null)
            {
                contourJson = "[]";
                return false;
            }

            return host.TryGetContourJson(out contourJson);
        }
    }
}
