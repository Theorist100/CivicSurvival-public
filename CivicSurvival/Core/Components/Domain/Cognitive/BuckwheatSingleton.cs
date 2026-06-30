using System;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// ECS Singleton for Buckwheat Protocol runtime state.
    /// Single Writer: BuckwheatSystem (runtime procurement/distribution).
    /// Config (ProcurementLevel) lives in BuckwheatConfig — written by the Cognitive request system.
    ///
    /// Dictionary state (district effects, cooldowns) remains in BuckwheatSystem
    /// because IComponentData must be unmanaged blittable.
    /// </summary>
    public struct BuckwheatSingleton : IComponentData
    {
        /// <summary>Current buckwheat reserve in tons.</summary>
        public float BuckwheatTons;

        /// <summary>Last procurement TotalGameHours (monotonic) for interval tracking.</summary>
        public float LastProcurementHour;

        /// <summary>Daily base cost at given procurement level (no sanctions markup).</summary>
        public static int DailyCost(int procurementLevel)
        {
            var cfg = BalanceConfig.Current.HumanitarianAid;
            return (int)Math.Round(cfg.TonsPerDayAt100 * procurementLevel / 100f * cfg.CostPerTon);
        }

        /// <summary>
        /// FIX S4-01: Daily cost with sanctions markup, computed per-interval to match
        /// actual charge rounding in BuckwheatSystem.ProcessProcurement.
        /// Single source of truth for both UI display and affordability checks.
        /// </summary>
        public static int DailyCostWithMarkup(int procurementLevel, float sanctionsMarkup)
        {
            var cfg = BalanceConfig.Current.HumanitarianAid;
            float interval = Math.Max(cfg.ProcurementIntervalHours, 0.1f);
            float intervalsPerDay = Math.Max(GameRate.HOURS_PER_DAY / interval, 1f);
            float tonsPerInterval = cfg.TonsPerDayAt100 * procurementLevel / 100f / intervalsPerDay;
            int baseCostPerInterval = (int)Math.Round(tonsPerInterval * cfg.CostPerTon);
            int effectiveCostPerInterval = SanctionsCostHelper.ApplyMarkup(baseCostPerInterval, sanctionsMarkup);
            return (int)Math.Round(effectiveCostPerInterval * intervalsPerDay);
        }

        /// <summary>True if player has at least required tons to distribute.</summary>
        public readonly bool CanDistribute =>
            BuckwheatTons >= BalanceConfig.Current.HumanitarianAid.TonsPerDistribution;

        public static BuckwheatSingleton Default => new()
        {
            BuckwheatTons = 0f,
            LastProcurementHour = float.MinValue
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.EnsurePaired(em, Default, BuckwheatConfig.Default, new EnsurePairedPolicy<BuckwheatSingleton, BuckwheatConfig>
            {
                MergeDuplicate = MergeDuplicateBuckwheatConfig,
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<BuckwheatConfig>(entity))
                em.AddComponentData(entity, BuckwheatConfig.Default);
        }

        private static void MergeDuplicateBuckwheatConfig(EntityManager em, Entity canonical, Entity duplicate)
        {
            if (!em.HasComponent<BuckwheatConfig>(duplicate) || !em.HasComponent<BuckwheatConfig>(canonical))
                return;

            var canonicalConfig = em.GetComponentData<BuckwheatConfig>(canonical);
            var duplicateConfig = em.GetComponentData<BuckwheatConfig>(duplicate);
            int defaultLevel = BuckwheatConfig.Default.ProcurementLevel;
            if (canonicalConfig.ProcurementLevel == defaultLevel
                && duplicateConfig.ProcurementLevel != defaultLevel)
            {
                canonicalConfig.ProcurementLevel = duplicateConfig.ProcurementLevel;
                em.SetComponentData(canonical, canonicalConfig);
            }
        }
    }
}
