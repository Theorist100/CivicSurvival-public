using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Moves wave-arrival/debris impact data into ThreatDamageSystem's apply queue.
    /// The apply system is scheduled separately after vanilla BuildingUpkeepSystem.
    /// </summary>
    [ActIndependent]
    public partial class ThreatDamageIntakeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatDamageIntakeSystem");

        private ThreatArrivalSystem? m_ArrivalSystem;
        private ThreatTerminalizationSystem? m_TerminalizationSystem;
        private DebrisSystem? m_DebrisSystem;
        private ThreatDamageSystem? m_ThreatDamageSystem;

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ResolveSystems();
        }

        protected override void OnUpdateImpl()
        {
            ResolveSystems();
            if (m_ArrivalSystem == null || m_TerminalizationSystem == null || m_DebrisSystem == null || m_ThreatDamageSystem == null)
                return;

            if (m_TerminalizationSystem.HasPendingImpacts
                && m_TerminalizationSystem.TryTransferPendingImpacts(out var terminalImpacts))
            {
                try
                {
                    m_ThreatDamageSystem.EnqueueApplyImpacts(terminalImpacts.AsArray());
                }
                finally
                {
                    m_TerminalizationSystem.CompletePendingImpactTransfer();
                }
            }

            var debrisImpacts = m_DebrisSystem.PendingDebrisImpacts;
            if (debrisImpacts.Length > 0)
            {
                m_ThreatDamageSystem.EnqueueApplyImpacts(debrisImpacts.AsArray());
                debrisImpacts.Clear();
            }

            if (!m_ArrivalSystem.HasPendingImpacts)
                return;

            if (!m_ArrivalSystem.TryTransferPendingImpacts(out var impacts))
                return;

            try
            {
                m_ThreatDamageSystem.EnqueueApplyImpacts(impacts.AsArray());
            }
            finally
            {
                m_ArrivalSystem.CompletePendingImpactTransfer();
            }
        }

        private void ResolveSystems()
        {
            m_ArrivalSystem ??= FeatureRegistry.Instance.Require<ThreatArrivalSystem>();
            m_TerminalizationSystem ??= FeatureRegistry.Instance.Require<ThreatTerminalizationSystem>();
            m_DebrisSystem ??= FeatureRegistry.Instance.Require<DebrisSystem>();
            m_ThreatDamageSystem ??= FeatureRegistry.Instance.Require<ThreatDamageSystem>();

            if (Log.IsDebugEnabled && (m_ArrivalSystem == null || m_TerminalizationSystem == null || m_DebrisSystem == null || m_ThreatDamageSystem == null))
                Log.Debug("Threat damage intake dependencies not ready");
        }
    }
}
