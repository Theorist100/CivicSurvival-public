using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Process-lifetime facade for vanilla lighting phase.
    /// </summary>
    [InfrastructureService]
    [Facade]
    public class LightingPhaseState : ILightingPhaseReader
    {
        private VanillaLightingAdapter? m_CurrentHost;

        internal VanillaLightingAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public bool IsReady => CurrentHost?.HasReadySnapshot == true;
        public string? UnavailableReason => CurrentHost?.UnavailableReason;
        public LightingPhase CurrentPhase => CurrentHost?.CurrentPhase ?? LightingPhase.Unknown;
        public bool IsDawnOrDuskLaunchWindow => CurrentHost?.IsDawnOrDuskLaunchWindow == true;
    }
}
