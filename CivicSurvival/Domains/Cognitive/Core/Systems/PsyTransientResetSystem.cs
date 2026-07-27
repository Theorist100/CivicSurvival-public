using Game;
using Game.Simulation;
using CivicSurvival.Core.Features.Wellbeing;
using Unity.Entities;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Jobs;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Resets HouseholdPsyState transients (Pressure_*, Exposure_*) after all readers are done.
    ///
    /// Execution order:
    /// 1. MentalHealthResolverSystem writes transients (on current PsySlot)
    /// 2. WellbeingResolverSystem reads persistent fields
    /// 3. THIS system resets transients for current slot only
    ///
    /// PsySlot filter: only resets entities MHR just processed (~43K instead of 172K).
    /// </summary>
#pragma warning disable CIVIC076 // Must run every frame to reset transients (ordering dependency)
    [ActIndependent]
    public partial class PsyTransientResetSystem : CivicSystemBase
#pragma warning restore CIVIC076
    {
        private static readonly LogContext Log = new("PsyTransientResetSystem");

        private EntityQuery m_HouseholdQuery;

        protected override string ProfileName => "PsyTransientReset.OnUpdate";

        protected override void OnCreate()
        {
            base.OnCreate();

            // PsySlot MUST be in query definition — without it, AddSharedComponentFilter
            // silently matches 0 chunks and ScheduleParallel processes 0 entities.
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadWrite<HouseholdPsyState>(),
                ComponentType.ReadOnly<PsySlot>()
            );

            Log.Info("Created (resets psy transients, PsySlot-filtered)");
        }

        protected override void OnUpdateImpl()
        {
            // Synchronized with MentalHealthResolverSystem — only reset when MHR fired
            if (!MentalHealthResolverSystem.DidFire)
                return;

            if (m_HouseholdQuery.IsEmptyIgnoreFilter)
                return;

            // Filter to current slot — only reset transients for entities MHR just processed
            m_HouseholdQuery.ResetFilter();
            m_HouseholdQuery.AddSharedComponentFilter(new PsySlot { SlotIndex = MentalHealthResolverSystem.CurrentSlot });

            var resetJob = new ResetHouseholdPsyTransientsJob();
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ResetHouseholdPsyTransientsJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} slot={MentalHealthResolverSystem.CurrentSlot}");
            Dependency = resetJob.ScheduleParallel(m_HouseholdQuery, Dependency);
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ResetHouseholdPsyTransientsJob.ScheduleParallel queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} slot={MentalHealthResolverSystem.CurrentSlot}");
        }
    }
}
