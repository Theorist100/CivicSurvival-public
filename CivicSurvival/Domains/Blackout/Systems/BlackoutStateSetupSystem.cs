using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Blackout.Systems
{
    /// <summary>
    /// Burst job to add BlackoutState (disabled) to buildings via parallel ECB.
    /// Seeds critical-infrastructure flags for new buildings; RefreshBlackoutStateJob
    /// keeps those flags current for existing buildings.
    ///
    /// IsCritical        = unconditional blackout exemption (police, fire, prison, etc.)
    /// HasBatteryPriority = battery discharges under CriticalOnly policy (hospital, school, fire, water)
    /// Hospital is excluded from IsCritical intentionally: it relies on battery, not unconditional exemption.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct AddBlackoutStateJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        // IsCritical lookups (unconditional blackout exemption)
        // Hospital excluded — battery-dependent (see HasBatteryPriority below)
        [ReadOnly] public ComponentLookup<Game.Buildings.FireStation> FireStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.PoliceStation> PoliceStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.Prison> PrisonLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.EmergencyShelter> EmergencyShelterLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.DeathcareFacility> DeathcareFacilityLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.WaterPumpingStation> WaterPumpingStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.SewageOutlet> SewageOutletLookup;

        // HasBatteryPriority lookups (battery discharge under CriticalOnly policy)
        [ReadOnly] public ComponentLookup<Game.Buildings.Hospital> HospitalLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.School> SchoolLookup;

        void Execute(Entity entity, [EntityIndexInQuery] int sortKey)
        {
            bool isCritical = FireStationLookup.HasComponent(entity)
                || PoliceStationLookup.HasComponent(entity)
                || PrisonLookup.HasComponent(entity)
                || EmergencyShelterLookup.HasComponent(entity)
                || DeathcareFacilityLookup.HasComponent(entity)
                || WaterPumpingStationLookup.HasComponent(entity)
                || SewageOutletLookup.HasComponent(entity);

            bool hasBatteryPriority = HospitalLookup.HasComponent(entity)
                || SchoolLookup.HasComponent(entity)
                || FireStationLookup.HasComponent(entity)
                || WaterPumpingStationLookup.HasComponent(entity);

            ECB.AddComponent(sortKey, entity, new BlackoutState
            {
                IsCritical = isCritical,
                HasBatteryPriority = hasBatteryPriority
            });
            ECB.SetComponentEnabled<BlackoutState>(sortKey, entity, false);
        }
    }

#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct RefreshBlackoutStateJob : IJobEntity
    {
        // IsCritical lookups (unconditional blackout exemption)
        // Hospital excluded — battery-dependent (see HasBatteryPriority below)
        [ReadOnly] public ComponentLookup<Game.Buildings.FireStation> FireStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.PoliceStation> PoliceStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.Prison> PrisonLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.EmergencyShelter> EmergencyShelterLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.DeathcareFacility> DeathcareFacilityLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.WaterPumpingStation> WaterPumpingStationLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.SewageOutlet> SewageOutletLookup;

        // HasBatteryPriority lookups (battery discharge under CriticalOnly policy)
        [ReadOnly] public ComponentLookup<Game.Buildings.Hospital> HospitalLookup;
        [ReadOnly] public ComponentLookup<Game.Buildings.School> SchoolLookup;

        void Execute(Entity entity, ref BlackoutState state)
        {
            state.IsCritical = FireStationLookup.HasComponent(entity)
                || PoliceStationLookup.HasComponent(entity)
                || PrisonLookup.HasComponent(entity)
                || EmergencyShelterLookup.HasComponent(entity)
                || DeathcareFacilityLookup.HasComponent(entity)
                || WaterPumpingStationLookup.HasComponent(entity)
                || SewageOutletLookup.HasComponent(entity);

            state.HasBatteryPriority = HospitalLookup.HasComponent(entity)
                || SchoolLookup.HasComponent(entity)
                || FireStationLookup.HasComponent(entity)
                || WaterPumpingStationLookup.HasComponent(entity);
        }
    }

    /// <summary>
    /// Adds BlackoutState component to buildings that can be blacked out.
    /// Component is added disabled (no blackout by default).
    ///
    /// Runs in Modification4 + ModificationBarrier4 (mirror of vanilla IgniteSystem,
    /// SystemOrder.cs:162). The first AddComponent&lt;BlackoutState&gt; on a new consumer
    /// building is the only structural change here; flushed at Modification4 it migrates the
    /// building's archetype BEFORE RequiredBatchesSystem and the render-side chunk-cache
    /// collection of the same frame. Done in GameSimulation the add played back out of phase
    /// with the render pass → zeroed render chunk-cache crash class. Modification4 (MainLoop)
    /// runs before GameSimulation (LateUpdate), so BlackoutState still exists before
    /// BlackoutSystem reads it — "components exist before blackout logic" invariant preserved.
    ///
    /// BlackoutState is NOT ISerializable — on load, no buildings have it.
    /// This system re-adds it to all eligible buildings (first throttle fire),
    /// then only processes new buildings (city growth).
    ///
    /// Throttled 500ms — new buildings wait at most half a second.
    /// IJobEntity + ECB.ParallelWriter — no main-thread sync point.
    /// </summary>
    [ActIndependent]
    public partial class BlackoutStateSetupSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("BlackoutStateSetupSystem");
        private EntityQuery m_MissingBlackoutQuery;
        private EntityQuery m_BlackoutStateQuery;
        private ModificationBarrier4 m_ECBSystem = null!;

        // IsCritical lookups (unconditional blackout exemption)
        private ComponentLookup<Game.Buildings.FireStation> m_FireStationLookup;
        private ComponentLookup<Game.Buildings.PoliceStation> m_PoliceStationLookup;
        private ComponentLookup<Game.Buildings.Prison> m_PrisonLookup;
        private ComponentLookup<Game.Buildings.EmergencyShelter> m_EmergencyShelterLookup;
        private ComponentLookup<Game.Buildings.DeathcareFacility> m_DeathcareFacilityLookup;
        private ComponentLookup<Game.Buildings.WaterPumpingStation> m_WaterPumpingStationLookup;
        private ComponentLookup<Game.Buildings.SewageOutlet> m_SewageOutletLookup;

        // HasBatteryPriority lookups (battery discharge under CriticalOnly policy)
        private ComponentLookup<Game.Buildings.Hospital> m_HospitalLookup;
        private ComponentLookup<Game.Buildings.School> m_SchoolLookup;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ECBSystem = World.GetOrCreateSystemManaged<ModificationBarrier4>();

            // Buildings with electricity consumer but WITHOUT our BlackoutState component.
            // No Created filter: BlackoutState is non-serializable, so after load ALL buildings
            // match this query (one-time bulk add). After that, only new buildings (city growth) match.
            // Critical infrastructure included - filtered in BlackoutJob based on ProtectCriticalInfra setting.
            // Power infrastructure (producers, batteries, transformers) always excluded.
            m_MissingBlackoutQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<ElectricityConsumer>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<ElectricityProducer>(),
                    ComponentType.ReadOnly<Game.Buildings.Battery>(),
                    ComponentType.ReadOnly<Game.Buildings.Transformer>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                },
                Absent = new[]
                {
                    ComponentType.ReadOnly<BlackoutState>()
                }
            });

            m_BlackoutStateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<ElectricityConsumer>(),
                    ComponentType.ReadWrite<BlackoutState>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<ElectricityProducer>(),
                    ComponentType.ReadOnly<Game.Buildings.Battery>(),
                    ComponentType.ReadOnly<Game.Buildings.Transformer>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            // IsCritical lookups (RO, reused by add + refresh jobs)
            m_FireStationLookup = GetComponentLookup<Game.Buildings.FireStation>(true);
            m_PoliceStationLookup = GetComponentLookup<Game.Buildings.PoliceStation>(true);
            m_PrisonLookup = GetComponentLookup<Game.Buildings.Prison>(true);
            m_EmergencyShelterLookup = GetComponentLookup<Game.Buildings.EmergencyShelter>(true);
            m_DeathcareFacilityLookup = GetComponentLookup<Game.Buildings.DeathcareFacility>(true);
            m_WaterPumpingStationLookup = GetComponentLookup<Game.Buildings.WaterPumpingStation>(true);
            m_SewageOutletLookup = GetComponentLookup<Game.Buildings.SewageOutlet>(true);

            // HasBatteryPriority lookups (RO, reused by add + refresh jobs)
            m_HospitalLookup = GetComponentLookup<Game.Buildings.Hospital>(true);
            m_SchoolLookup = GetComponentLookup<Game.Buildings.School>(true);

            Log.Info($"{nameof(BlackoutStateSetupSystem)} created (throttled 500ms, IJobEntity)");
        }

        protected override void OnThrottledUpdate()
        {
            // Update lookups for critical infra detection
            m_FireStationLookup.Update(this);
            m_PoliceStationLookup.Update(this);
            m_PrisonLookup.Update(this);
            m_EmergencyShelterLookup.Update(this);
            m_DeathcareFacilityLookup.Update(this);
            m_WaterPumpingStationLookup.Update(this);
            m_SewageOutletLookup.Update(this);
            m_HospitalLookup.Update(this);
            m_SchoolLookup.Update(this);

            // CIVIC186: skip scheduling on an empty query (wasted CPU / crash).
            if (!m_BlackoutStateQuery.IsEmpty)
            {
                var refreshJob = new RefreshBlackoutStateJob
                {
                    FireStationLookup = m_FireStationLookup,
                    PoliceStationLookup = m_PoliceStationLookup,
                    PrisonLookup = m_PrisonLookup,
                    EmergencyShelterLookup = m_EmergencyShelterLookup,
                    DeathcareFacilityLookup = m_DeathcareFacilityLookup,
                    WaterPumpingStationLookup = m_WaterPumpingStationLookup,
                    SewageOutletLookup = m_SewageOutletLookup,
                    HospitalLookup = m_HospitalLookup,
                    SchoolLookup = m_SchoolLookup
                };
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] pre RefreshBlackoutStateJob.ScheduleParallel queryEmpty=false");
                Dependency = refreshJob.ScheduleParallel(m_BlackoutStateQuery, Dependency);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post RefreshBlackoutStateJob.ScheduleParallel queryEmpty=false");
            }

            // Only allocate the command buffer + register the producer when the
            // add-job actually runs (it is the sole ECB writer here).
            if (!m_MissingBlackoutQuery.IsEmpty)
            {
                var ecb = m_ECBSystem.CreateCommandBuffer().AsParallelWriter();
                var addJob = new AddBlackoutStateJob
                {
                    ECB = ecb,
                    FireStationLookup = m_FireStationLookup,
                    PoliceStationLookup = m_PoliceStationLookup,
                    PrisonLookup = m_PrisonLookup,
                    EmergencyShelterLookup = m_EmergencyShelterLookup,
                    DeathcareFacilityLookup = m_DeathcareFacilityLookup,
                    WaterPumpingStationLookup = m_WaterPumpingStationLookup,
                    SewageOutletLookup = m_SewageOutletLookup,
                    HospitalLookup = m_HospitalLookup,
                    SchoolLookup = m_SchoolLookup
                };
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] pre AddBlackoutStateJob.ScheduleParallel queryEmpty=false ecb=true");
                Dependency = addJob.ScheduleParallel(m_MissingBlackoutQuery, Dependency);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post AddBlackoutStateJob.ScheduleParallel queryEmpty=false ecb=true");
                m_ECBSystem.AddJobHandleForProducer(Dependency);
            }
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            base.OnDestroy();
        }
    }
}
