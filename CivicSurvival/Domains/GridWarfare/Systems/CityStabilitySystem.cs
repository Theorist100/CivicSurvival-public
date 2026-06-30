using System;
using Colossal.Serialization.Entities;
using Game;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// Aggregates city health metrics into Stability score (0-100%).
    /// Stability provides attack cost discount (0-20%).
    ///
    /// Dimensions:
    /// - Physical (40%): Blackout %, destroyed buildings, fires
    /// - Digital (30%): Power deficit, grid stress
    /// - Social (30%): Happiness penalty, commerce penalty
    /// </summary>
    [ActIndependent]
    public partial class CityStabilitySystem : ThrottledSystemBase, IDefaultSerializable, IResettable
    {
        private static readonly LogContext Log = new("CityStabilitySystem");

        // R4-S6-06 FIX: Reduced from 150 to 30 — 1s staleness instead of 5s
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        // Co-fire with DamageStatsUpdateSystem so RegisterAfter] ordering is effective
        protected override string ThrottlePhaseKey => CivicSurvival.Core.Features.CrossDomain.DamageAccounting.DamageStatsUpdateSystem.PHASE_KEY;

        // Cached dependencies
        private DistrictPenaltySystem? m_PenaltySystem;
        private PlayerAttackSystem? m_PlayerAttackSystem;
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_DamageStatsQuery;
        [NonSerialized] private bool m_LoggedMissingPenaltySystem;

        // Current stability (0.0 - 1.0)
        private float m_Stability = 1f;

        // Config accessor (shorthand)
        private static CityStabilityConfig Cfg => BalanceConfig.Current.CityStability;

        public float Stability => m_Stability;
        public float StabilityPercent => m_Stability * 100f;
        public float Discount => m_Stability * math.clamp(Cfg.MaxDiscount, 0f, 1f);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_DamageStatsQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatDamageStatsSingleton>());
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_PlayerAttackSystem ??= FeatureRegistry.Instance.Require<PlayerAttackSystem>();
            ResolveOptionalPenalty();
            m_PlayerAttackSystem.SetStabilityDiscount(Discount);
        }

        // OD-004 FIX: Clear service references to break circular dependencies
        protected override void OnDestroy()
        {
            m_PenaltySystem = null;
            m_PlayerAttackSystem = null;
            base.OnDestroy();
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_LoggedMissingPenaltySystem = false;
            ResolveOptionalPenalty();
            m_PlayerAttackSystem?.SetStabilityDiscount(Discount);
        }

        private void ResolveOptionalPenalty()
        {
            m_PenaltySystem = FeatureRegistry.IsInitialized
                ? FeatureRegistry.Instance.Query<DistrictPenaltySystem>()
                : null;

            if (m_PenaltySystem == null && !m_LoggedMissingPenaltySystem)
            {
                m_LoggedMissingPenaltySystem = true;
                Log.Warn("DistrictPenaltySystem unavailable; city stability will use neutral social penalties");
            }
        }


        protected override void OnThrottledUpdate()
        {
            CalculateStability();

            // Update PlayerAttackSystem discount
            m_PlayerAttackSystem?.SetStabilityDiscount(Discount);
        }

        private void CalculateStability()
        {
            float physical = CalculatePhysicalDimension();
            float digital = CalculateDigitalDimension();
            float social = CalculateSocialDimension();

            // Weighted blend + invert (1.0 = perfect, 0.0 = crisis) — shared Core formula.
            m_Stability = CityAxisFormulas.StabilityFromAxes(
                physical, digital, social,
                Cfg.PhysicalWeight, Cfg.DigitalWeight, Cfg.SocialWeight);
        }

        /// <summary>
        /// Physical dimension: Blackout %, destroyed buildings, fires.
        /// Returns 0-1 instability factor. Gathers the city snapshot from ECS, then
        /// delegates the math to the shared <see cref="CityAxisFormulas.PhysicalInstability"/>.
        /// Absent sources pass zero counts (no penalty system → 0 affected districts; no
        /// damage-stats singleton → 0 destroyed/fires), which clamp to a zero contribution.
        /// </summary>
        private float CalculatePhysicalDimension()
        {
            int affectedDistricts = m_PenaltySystem != null ? m_PenaltySystem.AffectedDistricts : 0;

            int buildingsDestroyed = 0;
            int buildingsOnFire = 0;
            if (m_DamageStatsQuery.TryGetSingleton<ThreatDamageStatsSingleton>(out var damageStats))
            {
                buildingsDestroyed = damageStats.BuildingsDestroyed;
                buildingsOnFire = damageStats.BuildingsOnFire;
            }

            return CityAxisFormulas.PhysicalInstability(
                affectedDistricts, Cfg.TotalDistricts,
                buildingsDestroyed, Cfg.MaxDestroyedBuildings,
                buildingsOnFire, Cfg.MaxFires,
                Cfg.BlackoutSubWeight, Cfg.DestroyedSubWeight, Cfg.FiresSubWeight);
        }

        /// <summary>
        /// Digital dimension: Power deficit, grid stress.
        /// Returns 0-1 instability factor. Delegates the math to the shared
        /// <see cref="CityAxisFormulas.DigitalInstability"/>; no grid singleton → neutral 0.
        /// </summary>
        private float CalculateDigitalDimension()
        {
            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
                return 0f;

            return CityAxisFormulas.DigitalInstability(
                grid.Balance, grid.Consumption, grid.Status,
                Cfg.DeficitSubWeight, Cfg.StressSubWeight);
        }

        /// <summary>
        /// Social dimension: Happiness penalty.
        /// Returns 0-1 instability factor. Delegates the math to the shared
        /// <see cref="CityAxisFormulas.SocialInstability"/>; no penalty system → neutral 0.
        /// </summary>
        private float CalculateSocialDimension()
        {
#pragma warning disable CIVIC256 // Optional cross-feature dependency; null means neutral social penalties.
            if (m_PenaltySystem == null) return 0f;
#pragma warning restore CIVIC256

            // Happiness penalty (full 30% weight since commerce is implicit in happiness)
            return CityAxisFormulas.SocialInstability(
                m_PenaltySystem.MaxHappinessPenalty, PenaltyConfig.MAX_HAPPINESS_PENALTY);
        }
    }
}
