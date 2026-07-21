using System;

namespace CivicSurvival.Core.Components.Lifecycle
{
    /// <summary>
    /// Explicit refresh boundary for lookup bundles exposed through synchronous
    /// service APIs. The bundle owns the complete set of lookup updates needed by
    /// a service method, so helper code cannot refresh only one lookup and then
    /// read stale liveness/state side lookups.
    /// </summary>
    public sealed class CivicServiceLookups
    {
        private readonly Action m_Refresh;
        private readonly Func<int>? m_VersionProvider;
        [CivicSurvival.Core.Attributes.ServiceLookupRefreshCursor("Local service lookup refresh cursor; observes a provider version and does not publish a snapshot.")]
        private int m_LastRefreshVersion = int.MinValue;

        public CivicServiceLookups(Action refresh)
            : this(refresh, null)
        {
        }

        public CivicServiceLookups(Action refresh, Func<int>? versionProvider)
        {
            m_Refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            m_VersionProvider = versionProvider;
        }

        public void Refresh()
        {
            m_Refresh();
        }

        public void RefreshIfStale()
        {
            if (m_VersionProvider == null)
            {
                Refresh();
                return;
            }

            var version = m_VersionProvider();
            if (version == m_LastRefreshVersion)
                return;

            m_Refresh();
            m_LastRefreshVersion = version;
        }
    }
}
