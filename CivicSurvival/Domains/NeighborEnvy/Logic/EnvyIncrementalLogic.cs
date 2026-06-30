using Colossal.Logging;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Game.Buildings;
using CivicSurvival.Core.Components.Domain.NeighborEnvy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Config;
using CivicSurvival.Domains.NeighborEnvy.Data;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.NeighborEnvy.Logic
{
    /// <summary>
    /// Pure logic for incremental envy updates.
    /// Only processes dirty districts for efficiency.
    /// </summary>
    internal static class EnvyIncrementalLogic
    {
        private static readonly LogContext Log = new("NeighborEnvy");

        /// <summary>
        /// Envy detection radius in meters.
        /// </summary>
        public const float ENVY_RADIUS = Engine.NeighborEnvy.ENVY_RADIUS;

        /// <summary>
        /// Perform incremental update for dirty districts only.
        /// Returns (affected, processed) counts.
        /// </summary>
        public static (int affected, int processed) Execute(
            ref NeighborEnvyData envyData,
            EntityManager em)
        {
            envyData.ClearTemporary();
            if (envyData.DirtyDistrictSet.Count == 0)
                return (0, 0);

            float gridPowerThreshold = BalanceConfig.Current.PowerGrid.GridPowerThreshold;
            // FIX P1-NE-002: Use Balance constant for adjacent cells (3x3 grid)
            var keysBuffer = new NativeList<int>(Engine.NeighborEnvy.ADJACENT_CELLS_COUNT, Allocator.Temp);

            int processed = 0;
            int affected = 0;
            if (envyData.DirtyDistrictSet.Count == 0)
                return (affected, processed);

            // Single O(N) pass over all buildings — update PowerState for ALL (prevents staleness),
            // but only add dirty-district buildings to BuildingsToProcess for envy recalculation.
            foreach (var kvp in envyData.BuildingDistricts)
            {
                int districtIdx = kvp.Value;
                long entityKey = kvp.Key;

                if (!envyData.EntityMap.TryGetValue(entityKey, out Entity entity)
                    || !em.Exists(entity))
                {
                    continue;
                }

                bool isPowered = IsBuildingPowered(em, entity, gridPowerThreshold);
                envyData.SetPowerState(entity, isPowered);

                if (envyData.DirtyDistrictSet.Contains(districtIdx))
                    envyData.BuildingsToProcess.Add(entityKey);
            }

            // Drain the dirty queue (DequeueNextDirtyDistrict also clears DirtyDistrictSet entries)
            if (Log.IsDebugEnabled) Log.Debug($"[NeighborEnvy] Incremental: draining {envyData.DirtyDistrictSet.Count} dirty districts");
            while (envyData.DequeueNextDirtyDistrict() != -1) { /* drain */ }

            foreach (long entityKey in envyData.BuildingsToProcess)
            {
                if (!envyData.PowerState.TryGetValue(entityKey, out byte powerState))
                    continue;

                bool isPowered = powerState == 1;
                processed++;

                if (!envyData.BuildingPositions.TryGetValue(entityKey, out float3 position))
                    continue;

                if (!envyData.EntityMap.TryGetValue(entityKey, out Entity entity))
                    continue;

                if (!em.Exists(entity))
                {
                    // L2 FIX: Clean up ghost entry — prevents stale PowerState from causing
                    // false positive envy on neighbors until next full rebuild
                    envyData.UnregisterBuilding(entityKey);
                    continue;
                }

                // Determine envy state
                bool hasEnvy = false;
                if (!isPowered)
                {
                    hasEnvy = HasPoweredNeighbor(ref envyData, em, position, entityKey, ref keysBuffer);
                }

#pragma warning disable CIVIC485 // Presence check selects AddComponent vs enable/disable for this enableable marker.
                bool hasComponent = em.HasComponent<EnvyAffected>(entity);
#pragma warning restore CIVIC485

                if (hasEnvy)
                {
                    if (hasComponent)
                    {
                        em.SetComponentEnabled<EnvyAffected>(entity, true); // Bit flip, no chunk move
                        affected++;
                    }
                    // else: not yet seeded by EnvyAffectedSetupSystem (Modification4). Skip enabling
                    // this tick — seeded (disabled) on the next setup throttle, enabled on the
                    // following update. Keeps this GameSimulation path free of any structural add
                    // (render chunk-cache crash class — enforced by CIVIC520).
                }
                else if (hasComponent)
                {
                    em.SetComponentEnabled<EnvyAffected>(entity, false); // Bit flip, no chunk move
                }
            }

            if (keysBuffer.IsCreated) keysBuffer.Dispose();

            if (processed > 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[NeighborEnvy] Incremental: {processed} processed, {affected} affected");
            }

            return (affected, processed);
        }

        private static bool IsBuildingPowered(EntityManager em, Entity entity, float gridPowerThreshold)
        {
            if (!em.HasComponent<ElectricityConsumer>(entity))
                return false;

            var consumer = em.GetComponentData<ElectricityConsumer>(entity);
            // CIVIC485: paired immediately with IsComponentEnabled before reading current state.
#pragma warning disable CIVIC485
            bool hasBlackoutState = em.HasComponent<BlackoutState>(entity);
#pragma warning restore CIVIC485
            var blackoutState = hasBlackoutState ? em.GetComponentData<BlackoutState>(entity) : default;
            bool isBlackoutEnabled = hasBlackoutState && em.IsComponentEnabled<BlackoutState>(entity);
            return EnvyPowerStateLogic.IsBuildingPowered(
                consumer,
                hasBlackoutState,
                isBlackoutEnabled,
                blackoutState,
                gridPowerThreshold);
        }

        /// <summary>
        /// Check if a building at position has any powered neighbors within ENVY_RADIUS.
        /// </summary>
        private static bool HasPoweredNeighbor(
            ref NeighborEnvyData envyData,
            EntityManager em,
            float3 position,
            long excludeEntityKey,
            ref NativeList<int> keysBuffer)
        {
            keysBuffer.Clear();
            NeighborEnvyData.GetAdjacentGridKeys(position, ref keysBuffer);

            foreach (int gridKey in keysBuffer)
            {
                if (!envyData.SpatialGrid.TryGetFirstValue(gridKey, out long neighborKey, out var iterator))
                    continue;

                do
                {
                    if (neighborKey == excludeEntityKey)
                        continue;

                    if (!envyData.EntityMap.TryGetValue(neighborKey, out Entity neighborEntity)
                        || !em.Exists(neighborEntity))
                        continue;

                    // Check if neighbor is powered
                    if (!envyData.PowerState.TryGetValue(neighborKey, out byte powerState))
                        continue;

                    if (powerState == 0)
                        continue; // Not powered

                    // Check distance
                    if (!envyData.BuildingPositions.TryGetValue(neighborKey, out float3 neighborPos))
                        continue;

#pragma warning disable CIVIC078 // Early-exit pattern; ENVY_RADIUS constant — sqrt negligible
                    float distance = math.distance(position, neighborPos);
#pragma warning restore CIVIC078
                    if (distance <= ENVY_RADIUS)
                    {
                        return true; // Found powered neighbor!
                    }

                } while (envyData.SpatialGrid.TryGetNextValue(out neighborKey, ref iterator));
            }

            return false;
        }
    }
}
