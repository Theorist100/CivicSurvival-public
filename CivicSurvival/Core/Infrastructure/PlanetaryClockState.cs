using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Interfaces.Services;

namespace CivicSurvival.Core.Infrastructure
{
    [InfrastructureService]
    [Facade]
    public class PlanetaryClockState : IPlanetaryClockReader
    {
        private VanillaPlanetaryClockAdapter? m_CurrentHost;

        internal VanillaPlanetaryClockAdapter? CurrentHost
        {
            get => System.Threading.Volatile.Read(ref m_CurrentHost);
            set => System.Threading.Volatile.Write(ref m_CurrentHost, value);
        }

        public bool IsReady => CurrentHost?.IsBound == true;
        public string? UnavailableReason => CurrentHost?.UnavailableReason;

        public bool TryGetClock(out int dayOfYear, out float currentHour)
        {
            var host = CurrentHost;
            if (host == null)
            {
                dayOfYear = 0;
                currentHour = 0f;
                return false;
            }

            return host.TryReadClock(out dayOfYear, out currentHour);
        }
    }
}
