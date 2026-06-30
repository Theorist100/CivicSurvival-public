using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types.Snapshots;
using Unity.Mathematics;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for vanilla terrain bounds.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class MapBoundsState : IMapBoundsReader, ITerrainHeightReader
    {
        private VanillaTerrainAdapter? m_CurrentHost;

        internal VanillaTerrainAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public bool IsReady => CurrentHost?.HasPublishedSnapshot == true;
        public string? UnavailableReason => CurrentHost?.UnavailableReason;

        public bool TryGetBounds(out MapBoundsSnapshot snapshot, out uint version)
        {
            var host = CurrentHost;
            if (host == null)
            {
                snapshot = MapBoundsSnapshot.Default;
                version = 0;
                return false;
            }

            return host.TryGetSnapshot(out snapshot, out version);
        }

        public bool TrySampleHeight(float3 worldPosition, out float height)
        {
            var host = CurrentHost;
            if (host == null)
            {
                height = 0f;
                return false;
            }

            return host.TrySampleHeight(worldPosition, out height);
        }
    }
}
