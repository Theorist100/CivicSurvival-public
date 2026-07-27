using System.Collections.Generic;
using Unity.Mathematics;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Domains.Narrative.Resolvers
{
    /// <summary>
    /// Owns the pending-event aggregation state used by ThreatNarrativeResolver.
    /// Holds three batch aggregators (impacts, intercepts, debris) plus accumulated
    /// power-plant damage. Pure state container — does not emit; the resolver pulls
    /// ready batches and forwards them to the static emitter.
    /// </summary>
    internal sealed class ThreatNarrativeBatcher
    {
        private readonly BatchAggregator<ThreatImpactEvent> m_PendingImpacts = new(BatchIdentityPolicy.NoDedup<ThreatImpactEvent>());
        private readonly BatchAggregator<float3> m_PendingDebris = new(BatchIdentityPolicy.NoDedup<float3>());
        private readonly BatchAggregator<ThreatInterceptEvent> m_PendingIntercepts = new(BatchIdentityPolicy.NoDedup<ThreatInterceptEvent>());

        private int m_UnreportedDamageMW;
        private int m_LastReportedRemainingMW;

        public int PendingImpactCount => m_PendingImpacts.Count;
        public int PendingDebrisCount => m_PendingDebris.Count;
        public int PendingInterceptCount => m_PendingIntercepts.Count;

        public bool QueueImpact(ThreatImpactEvent evt)
        {
#pragma warning disable CIVIC230 // NoDedup policy intentionally preserves every impact event
            return m_PendingImpacts.Add(evt);
#pragma warning restore CIVIC230
        }

        public bool QueueIntercept(ThreatInterceptEvent evt)
        {
#pragma warning disable CIVIC230 // NoDedup policy intentionally preserves every intercept event
            return m_PendingIntercepts.Add(evt);
#pragma warning restore CIVIC230
        }

        public bool QueueDebris(float3 position) => m_PendingDebris.Add(position);

        public IReadOnlyList<ThreatImpactEvent> ForceFlushImpacts() => m_PendingImpacts.ForceFlush();
        public IReadOnlyList<ThreatInterceptEvent> ForceFlushIntercepts() => m_PendingIntercepts.ForceFlush();
        public IReadOnlyList<float3> ForceFlushDebris() => m_PendingDebris.ForceFlush();

        public bool IsImpactsReadyToFlush() => m_PendingImpacts.IsReadyToFlush();
        public bool IsInterceptsReadyToFlush() => m_PendingIntercepts.IsReadyToFlush();
        public bool IsDebrisReadyToFlush() => m_PendingDebris.IsReadyToFlush();

        public IReadOnlyList<ThreatImpactEvent> FlushReadyImpacts() => m_PendingImpacts.FlushAndGet();
        public IReadOnlyList<ThreatInterceptEvent> FlushReadyIntercepts() => m_PendingIntercepts.FlushAndGet();
        public IReadOnlyList<float3> FlushReadyDebris() => m_PendingDebris.FlushAndGet();

        public void ClearImpacts() => m_PendingImpacts.Clear();
        public void ClearIntercepts() => m_PendingIntercepts.Clear();
        public void ClearDebris() => m_PendingDebris.Clear();

        /// <summary>
        /// Accumulate small power-plant damage and report current remaining MW. Returns
        /// true and the report value when accumulated damage crosses
        /// Engine.Threats.MIN_SIGNIFICANT_LOSS_MW; otherwise returns false.
        /// </summary>
        public bool AccumulateDamage(int lostMW, int remainingMW, int significantThreshold, out int reportMW)
        {
            m_LastReportedRemainingMW = remainingMW;
            m_UnreportedDamageMW += lostMW;

            if (m_UnreportedDamageMW < significantThreshold)
            {
                reportMW = 0;
                return false;
            }

            reportMW = m_UnreportedDamageMW;
            m_UnreportedDamageMW = 0;
            return true;
        }

        /// <summary>
        /// Pull any pending damage that didn't cross the threshold. Returns false if
        /// nothing is buffered.
        /// </summary>
        public bool TryConsumePendingDamage(out int lostMW, out int remainingMW)
        {
            if (m_UnreportedDamageMW <= 0)
            {
                lostMW = 0;
                remainingMW = 0;
                return false;
            }

            lostMW = m_UnreportedDamageMW;
            remainingMW = m_LastReportedRemainingMW;
            m_UnreportedDamageMW = 0;
            return true;
        }

        public void Reset()
        {
            m_PendingImpacts.Clear();
            m_PendingDebris.Clear();
            m_PendingIntercepts.Clear();
            m_UnreportedDamageMW = 0;
            m_LastReportedRemainingMW = 0;
        }
    }
}
