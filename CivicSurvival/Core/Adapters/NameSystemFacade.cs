using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;

namespace CivicSurvival.Core.Adapters
{
    /// <summary>
    /// Process-lifetime façade over <see cref="NameSystemHost"/>. Lives in
    /// <see cref="ServiceRegistry"/>; the underlying vanilla
    /// <c>Game.UI.NameSystem</c> ref is held by the world-owned host.
    /// When <see cref="CurrentHost"/> is null (boot/teardown window), lookups
    /// return <see cref="string.Empty"/>.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public sealed class NameSystemFacade
    {
        private NameSystemHost? m_CurrentHost;

        internal NameSystemHost? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public string GetRenderedLabelName(Entity entity)
        {
            if (entity == Entity.Null) return string.Empty;
            return CurrentHost?.GetRenderedLabelName(entity) ?? string.Empty;
        }
    }
}
