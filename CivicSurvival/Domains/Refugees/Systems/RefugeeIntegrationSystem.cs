using System;
using System.Collections.Generic;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Domains.Refugees.Data;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Integrates refugees from parks into permanent housing.
    /// Runs when available residential space exists and refugees are in parks.
    ///
    /// Flow:
    /// 1. Query HomelessHousehold entities where m_TempHome is Park
    /// 2. If vacant residential buildings exist, add PropertySeeker component
    /// 3. Game's natural PropertyRenterSystem handles the rest
    /// 4. Post Chirper message about integration progress
    ///
    /// This system enables the game's natural housing mechanics for refugees
    /// who have been processed through the park shelter stage.
    /// </summary>
    [ActIndependent]
    public partial class RefugeeIntegrationSystem : CivicSystemBase
    {
        private const int BATCH_INDEX_WRAP = 10000;

        private static readonly LogContext Log = new("RefugeeIntegrationSystem");

        private EntityQuery m_ParkQuery;
        private EntityQuery m_HomelessAtParkQuery;
        private EntityQuery m_VacantResidentialQuery;
        private EntityQuery m_HomelessSeekingHousingQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<RefugeeHousehold> m_RefugeeLookup;
        private ComponentLookup<PropertySeeker> m_PropertySeekerLookup;

        private double m_LastCheckGameHours;
        private int m_IntegrationBatchIndex;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_RefugeeLookup = GetComponentLookup<RefugeeHousehold>(true);
            m_PropertySeekerLookup = GetComponentLookup<PropertySeeker>(true);

            m_ParkQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Park>(),
                ComponentType.Exclude<Deleted>()
            );

            // Gate query: homeless households that might need integration.
            // Do NOT use Exclude<PropertySeeker> — it's archetype-level and filters out
            // refugees with disabled PropertySeeker (IEnableableComponent). We need those.
            m_HomelessAtParkQuery = GetEntityQuery(
                ComponentType.ReadOnly<HomelessHousehold>(),
                ComponentType.Exclude<Deleted>()
            );

            // Query for residential buildings with available space
            m_VacantResidentialQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<PropertyOnMarket>(),
                ComponentType.Exclude<Deleted>()
            );

            // Global housing pressure: vanilla seekers and refugees compete for the same vacant homes.
            m_HomelessSeekingHousingQuery = GetEntityQuery(
                ComponentType.ReadOnly<HomelessHousehold>(),
                ComponentType.ReadOnly<PropertySeeker>(),
                ComponentType.Exclude<Deleted>()
            );

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            m_RefugeeLookup.Update(this);
            m_PropertySeekerLookup.Update(this);
            // LOAD-INVARIANT: OnUpdate can run before GameTime activation on the first loaded frame.
            if (!GameTimeSystem.TryGetGameHours(out var currentGameHours))
                return;
            // BUG-R-016 FIX: Use Balance constant instead of magic number
            // R3-C-11: Game-hours throttle means refugee data is "stale" between checks.
            // At check time, all EntityQuery counts are fresh. Staleness is the check interval
            // itself (config-driven, typically hours of game time) — by design for performance.
            var scenarioCfg = BalanceConfig.Current.Scenario;
            if (currentGameHours - m_LastCheckGameHours < scenarioCfg.IntegrationCheckIntervalHours)
                return;

            m_LastCheckGameHours = currentGameHours;

            // Skip if no parks
            if (m_ParkQuery.IsEmptyIgnoreFilter)
                return;

            // Skip if no homeless at parks
            if (m_HomelessAtParkQuery.IsEmptyIgnoreFilter)
                return;

            // Skip if no vacant residential
            if (m_VacantResidentialQuery.IsEmptyIgnoreFilter)
                return;

            // S15-9 FIX: Prevent adding PropertySeeker when housing already saturated.
            // Count how many refugees are ALREADY seeking housing (have PropertySeeker).
            // Use conservative multiplier (×2 instead of ×5) — buildings may be fully occupied.
            // Cap new seekers to remaining capacity gap to prevent MoveAway cascade.
            CountForCapacity(m_HomelessSeekingHousingQuery, out int alreadySeekingCount);
            CountForCapacity(m_VacantResidentialQuery, out int vacantBuildingCount);

            int estimatedVacantCapacity = vacantBuildingCount * 2;
            int remainingCapacity = estimatedVacantCapacity - alreadySeekingCount;
            if (remainingCapacity <= 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Skipping integration: {alreadySeekingCount} already seeking vs ~{estimatedVacantCapacity} capacity ({vacantBuildingCount} buildings)");
                return;
            }

            // Build park entity set for filtering
            int parkCapacity = CountForCapacity(m_ParkQuery);
            var parkSet = new NativeHashSet<Entity>(math.max(parkCapacity, 4), Allocator.Temp);
            foreach (var (_, parkEntity) in
                SystemAPI.Query<RefRO<Park>>()
                .WithAll<Building>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                parkSet.Add(parkEntity);
            }

            // Count refugee homeless at parks (first pass — only OUR refugees, not vanilla homeless)
            // Use WithPresent<PropertySeeker> to include refugees with disabled PS (IEnableableComponent).
            // WithNone<PropertySeeker> is archetype-level in source-gen and would exclude them.
            int totalAtPark = 0;
            foreach (var (homelessRef, entity) in
                SystemAPI.Query<RefRO<HomelessHousehold>>()
                .WithPresent<PropertySeeker>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                if (parkSet.Contains(homelessRef.ValueRO.m_TempHome) && m_RefugeeLookup.HasComponent(entity))
                    totalAtPark++;
            }

            if (totalAtPark == 0)
            {
                if (parkSet.IsCreated) parkSet.Dispose();
                return;
            }

            // BUG-R-009 FIX: Use Unity.Mathematics.math.max instead of System.Math
            int toIntegrate = math.max(1, (int)Math.Round(totalAtPark * scenarioCfg.IntegrationRate));
            // S15-9 FIX: Never add more seekers than estimated remaining capacity
            toIntegrate = math.min(toIntegrate, remainingCapacity);
            int integratedCount = 0;

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            // Re-enable PropertySeeker to enable natural housing search (second pass with limit)
            // PropertySeeker is IEnableableComponent — disabled by RefugeeSpawnService, re-enabled here
            foreach (var (homelessRef, entity) in
                SystemAPI.Query<RefRO<HomelessHousehold>>()
                .WithPresent<PropertySeeker>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                if (integratedCount >= toIntegrate)
                    break;

                // Check if this refugee household is at a park, PropertySeeker is disabled, and is our refugee
                if (parkSet.Contains(homelessRef.ValueRO.m_TempHome)
                    && m_RefugeeLookup.HasComponent(entity)
                    && m_PropertySeekerLookup.HasComponent(entity) && !m_PropertySeekerLookup.IsComponentEnabled(entity))
                {
                    if (!hasEcb)
                    {
                        ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                        hasEcb = true;
                    }
                    ecb.SetComponentEnabled<PropertySeeker>(entity, true);
                    integratedCount++;
                }
            }

            if (parkSet.IsCreated) parkSet.Dispose();

            if (integratedCount > 0)
            {
                // BUG-R-017 FIX: Reset batch index on overflow to prevent int32 wraparound
                m_IntegrationBatchIndex = (m_IntegrationBatchIndex + 1) % BATCH_INDEX_WRAP;

                // Post Chirper message about integration
                // BUG-R-014 FIX: Reduce allocations with helper method for single-arg events
                var eventArgs = NarrativeArgs.OneArg(integratedCount);
                EventBus?.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.RefugeeIntegrated.ToKey(),
                    eventArgs
                ), "RefugeeIntegrationSystem");

                // BUG-R-015 FIX: Use Debug for frequent internal tracking logs
                if (Log.IsDebugEnabled) Log.Debug($"Enabled housing search for {integratedCount} households ({totalAtPark - integratedCount} remaining in parks)");
            }
        }
    }
}
