using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Singleton entity that owns ImpactDistrictEntry buffer.
    /// Written by: ThreatDamageSystem (appends per impact)
    /// Read+cleared by: MentalHealthResolverSystem (snapshot every 8 frames)
    ///
    /// Own singleton follows existing pattern:
    /// - BackupPowerStateSingleton owns DistrictBatteryCoverage
    /// - IPSOState owns IPSODistrictExposureBuffer
    /// - SpotterCountermeasuresState owns InternetDisabledBuffer
    /// </summary>
    [CivicSingleton]
    public struct ImpactPressureSingleton : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(ImpactPressureSingleton), new EnsureSingletonPolicy<ImpactPressureSingleton>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<ImpactDistrictEntry>(entity))
                em.AddBuffer<ImpactDistrictEntry>(entity);
        }
    }

    /// <summary>
    /// Buffer element: one recent impact's district + intensity.
    /// Ephemeral — snapshot+cleared by MHR every 8 frames, no serialization.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct ImpactDistrictEntry : IBufferElementData
    {
#pragma warning disable CIVIC262 // Session-local runtime district key; NOT persisted (ephemeral, rebuilt per tick)
        /// <summary>District index of impact location</summary>
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Impact intensity (0-1, clamped from ThreatImpactData.Severity)</summary>
        public float Intensity;

        /// <summary>
        /// Target social stratum. <see cref="SocialStratum.Unknown"/> = a
        /// stratum-agnostic physical impact (e.g. <c>ThreatDamageSystem</c> rocket hit) that
        /// reaches every household as trauma. A non-Unknown value = a discrete PSYOPS impact
        /// that reaches only that stratum's households and feeds cognitive infection.
        /// </summary>
        public SocialStratum TargetStratum;

        /// <summary>
        /// Carrier of a discrete PSYOPS impact — read ONLY when
        /// <see cref="TargetStratum"/> != <see cref="SocialStratum.Unknown"/>. The active hero
        /// archetype dampens infection from its counter-type (rock-paper-scissors); the resolver
        /// keys that damping off this field. Defaults to <see cref="PsyOpsAttackType.Propaganda"/>
        /// (the zero value) for physical impacts, where it is never read.
        /// </summary>
        public PsyOpsAttackType AttackType;

        /// <summary>Physical (stratum-agnostic) impact — reaches all strata as trauma.</summary>
        public static ImpactDistrictEntry Create(int districtIndex, float intensity) =>
            Create(districtIndex, intensity, SocialStratum.Unknown, PsyOpsAttackType.Propaganda);

        /// <summary>Stratum-targeted impact (PSYOPS); Unknown = physical/all-strata. Carrier type defaults to Propaganda (the inert value for physical impacts).</summary>
        public static ImpactDistrictEntry Create(int districtIndex, float intensity, SocialStratum targetStratum) =>
            Create(districtIndex, intensity, targetStratum, PsyOpsAttackType.Propaganda);

        /// <summary>Discrete PSYOPS impact carrying its attack type (hero-by-type RPS damping).</summary>
        public static ImpactDistrictEntry Create(int districtIndex, float intensity, SocialStratum targetStratum, PsyOpsAttackType attackType) => new()
        {
            // District identity is the raw entity index (Unzoned = 0); never a
            // small dense index. Only floor stray negatives to the Unzoned
            // bucket — the old 0..499 clamp mis-attributed every impact in a
            // district entity-index ≥500 to district 499 (G7 A-5).
            DistrictIndex = math.max(districtIndex, 0),
            Intensity = math.saturate(intensity),
            TargetStratum = targetStratum,
            AttackType = attackType
        };
    }
}
