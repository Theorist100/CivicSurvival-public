using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Batched damage/casualty reporting for wave debriefing.
    /// Hybrid approach: collect reports in lists, flush once per frame.
    /// Avoids per-damage EntityManager access.
    /// </summary>
    public struct DebriefingBatcher : System.IDisposable
    {
        private NativeList<DamageBatch> m_PendingDamage;
        private NativeList<int> m_PendingCasualties;
        private int m_TotalCost;
        private int m_Destroyed;
        private int m_OnFire;
        private int m_Casualties;

        /// <summary>
        /// Single damage report entry.
        /// </summary>
        public struct DamageBatch
        {
            public int Cost;
            public int Destroyed;
            public int OnFire;
        }

        /// <summary>
        /// Create batcher with pre-allocated lists.
        /// </summary>
        public static DebriefingBatcher Create(int capacity = 64)
        {
            return new DebriefingBatcher
            {
                m_PendingDamage = new NativeList<DamageBatch>(capacity, Allocator.Persistent),
                m_PendingCasualties = new NativeList<int>(capacity, Allocator.Persistent)
            };
        }

        public bool IsCreated => m_PendingDamage.IsCreated;

        /// <summary>
        /// Clear pending reports at frame start.
        /// </summary>
        public void Clear()
        {
            m_PendingDamage.Clear();
            m_PendingCasualties.Clear();
            m_TotalCost = 0;
            m_Destroyed = 0;
            m_OnFire = 0;
            m_Casualties = 0;
        }

        /// <summary>
        /// Report building destroyed with cost.
        /// </summary>
        public void ReportDestroyed(int cost)
        {
            m_PendingDamage.Add(new DamageBatch
            {
                Cost = cost,
                Destroyed = 1,
                OnFire = 0
            });
            m_TotalCost += cost;
            m_Destroyed++;
        }

        /// <summary>
        /// Report building caught fire.
        /// </summary>
        public void ReportFire()
        {
            m_PendingDamage.Add(new DamageBatch
            {
                Cost = 0,
                Destroyed = 0,
                OnFire = 1
            });
            m_OnFire++;
        }

        /// <summary>
        /// Report casualties.
        /// </summary>
        public void ReportCasualties(int count)
        {
            if (count > 0)
            {
                m_PendingCasualties.Add(count);
                m_Casualties += count;
            }
        }

        /// <summary>
        /// Check if there are pending reports to flush.
        /// </summary>
        public bool HasPendingReports => !m_PendingDamage.IsEmpty || !m_PendingCasualties.IsEmpty;

        /// <summary>
        /// Get aggregated totals for flushing.
        /// </summary>
        public (int totalCost, int destroyed, int onFire, int casualties) GetTotals()
        {
            return (m_TotalCost, m_Destroyed, m_OnFire, m_Casualties);
        }

        public void Dispose()
        {
            if (m_PendingDamage.IsCreated) m_PendingDamage.Dispose();
            if (m_PendingCasualties.IsCreated) m_PendingCasualties.Dispose();
        }
    }

    /// <summary>
    /// Static utilities for damage statistics.
    /// </summary>
    public static class DamageStatsHelper
    {
        /// <summary>
        /// Flush batched reports to DebriefingDamageStats singleton.
        /// </summary>
        public static void FlushToDebriefing(
            ref DebriefingBatcher batcher,
            EntityQuery debriefingQuery,
            ref ComponentLookup<DebriefingDamageStats> debriefingLookup)
        {
            if (!batcher.HasPendingReports) return;

            if (!debriefingQuery.TryGetSingletonEntity<DebriefingDamageStats>(out var entity))
                return;
            if (!debriefingLookup.TryGetComponent(entity, out var data))
                return;

            var (totalCost, destroyed, onFire, casualties) = batcher.GetTotals();

            data.DamageCost += totalCost;
            data.BuildingsDestroyed += destroyed;
            data.BuildingsOnFire += onFire;
            data.Casualties += casualties;

            debriefingLookup[entity] = data;
        }

        // UpdateStatsSingleton moved to Core/Systems/Domain/Threats/DamageStatsUpdateSystem.cs (M-57)
    }
}

