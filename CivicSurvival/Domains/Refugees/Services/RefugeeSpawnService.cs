using Colossal.Serialization.Entities;
using System.Collections.Generic;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Data;

namespace CivicSurvival.Domains.Refugees.Services
{
    /// <summary>
    /// Service for spawning refugee households.
    /// Uses CS2's native HouseholdSpawnSystem + HouseholdInitializeSystem flow,
    /// then marks households as homeless refugees.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.RefugeesName)]
    public sealed class RefugeeSpawnService : System.IDisposable
    {
        private static readonly LogContext Log = new("RefugeeSpawnService");

        public readonly struct HouseholdPrefabChoice
        {
            public readonly Entity Prefab;
            public readonly EntityArchetype Archetype;

            public HouseholdPrefabChoice(Entity prefab, EntityArchetype archetype)
            {
                Prefab = prefab;
                Archetype = archetype;
            }
        }

        private readonly World m_World;
        private Unity.Mathematics.Random m_Random;

        private EntityQuery m_ParkQuery;
        private EntityQuery m_OutsideConnectionQuery;

        private bool m_Initialized;

        public RefugeeSpawnService(World world)
        {
            m_World = world;
            // Random will be initialized in Initialize() when TimeSystem is ready
        }

        /// <summary>
        /// Initialize queries (call once when game is ready).
        /// </summary>
        public void Initialize()
        {
            if (m_Initialized) return;
            m_Disposed = false;

            // BUG-R-004 FIX: Initialize Random with null-safe seed
            uint seed;
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider != null)
            {
                seed = (uint)(timeProvider.Current.TotalGameHours * GameRate.SECONDS_PER_HOUR)
                       ^ (uint)System.Environment.TickCount;
            }
            else
            {
                // Fallback: use Environment.TickCount for non-deterministic but safe seed
                seed = (uint)System.Environment.TickCount;
                Log.Warn("GameTimeSystem not ready, using fallback seed");
            }

            m_Random = new Unity.Mathematics.Random(seed == 0 ? 1 : seed);  // Never use seed 0
            Log.Info($"Random initialized with seed {seed}");

            var em = m_World.EntityManager;

            // Parks for shelter
            m_ParkQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Game.Buildings.Park>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>()
            );

            // Border connections
            m_OutsideConnectionQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>()
            );

            m_Initialized = true;
            Log.Info("Initialized");
        }

        /// <summary>
        /// Spawn refugee households.
        /// Main thread only: resolves spawn point, mutates service RNG, and writes to the provided ECB.
        /// Prefer the overload with a caller-owned prefab snapshot.
        /// </summary>
        /// <param name="count">Number of households to spawn</param>
        /// <param name="spawnAtBorder">True = border, False = park</param>
        /// <param name="ecb">EntityCommandBuffer for deferred entity creation</param>
        /// <returns>Number queued for spawn</returns>
        public int SpawnRefugees(int count, bool spawnAtBorder, EntityCommandBuffer ecb)
            => SpawnRefugees(count, spawnAtBorder, Entity.Null, ecb, null);

        public int SpawnRefugees(int count, bool spawnAtBorder, Entity preSelectedSpawn, EntityCommandBuffer ecb)
            => SpawnRefugees(count, spawnAtBorder, preSelectedSpawn, ecb, null);

        /// <summary>
        /// Spawn refugee households from a caller-owned prefab snapshot.
        /// Main thread only: mutates service RNG and writes to the provided ECB.
        /// </summary>
        public int SpawnRefugees(
            int count,
            bool spawnAtBorder,
            Entity preSelectedSpawn,
            EntityCommandBuffer ecb,
            IReadOnlyList<HouseholdPrefabChoice>? prefabChoices)
        {
            if (!m_Initialized)
            {
                Log.Warn("Not initialized");
                return 0;
            }

            // Use pre-selected spawn point or resolve independently
            Entity spawnPoint = preSelectedSpawn != Entity.Null ? preSelectedSpawn : GetSpawnPoint(spawnAtBorder);
            if (spawnPoint == Entity.Null)
            {
                Log.Warn($"No spawn point available (border={spawnAtBorder})");
                return 0;
            }

            if (prefabChoices == null || prefabChoices.Count == 0)
            {
                Log.Warn("No household prefabs found");
                return 0;
            }

            // Vanilla counts shelter residents via the building's Renter buffer
            // (ResidentsSection / PropertyProcessingSystem), and Game.Serialization.
            // RenterSystem rebuilds that buffer from HomelessHousehold.m_TempHome on
            // every load — so registering here keeps the live state identical to the
            // post-load canonical state. OutsideConnections have no Renter buffer;
            // vanilla tolerates that and so do we.
            bool spawnPointHasRenterBuffer = m_World.EntityManager.HasBuffer<Game.Buildings.Renter>(spawnPoint);

            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                // Select random prefab
                int idx = m_Random.NextInt(prefabChoices.Count);
                var choice = prefabChoices[idx];

                // Create household entity via ECB (same as HouseholdSpawnSystem)
                Entity household = ecb.CreateEntity(choice.Archetype);

                // Set PrefabRef (required for HouseholdInitializeSystem)
                ecb.SetComponent(household, new PrefabRef { m_Prefab = choice.Prefab });

                // Add CurrentBuilding (triggers HouseholdInitializeSystem)
                ecb.AddComponent(household, new CurrentBuilding { m_CurrentBuilding = spawnPoint });

                // Mark as refugee homeless (will be processed after HouseholdInitializeSystem)
                // HomelessHousehold prevents MoveAway
                ecb.AddComponent(household, new HomelessHousehold { m_TempHome = spawnPoint });

                // Marker for efficient processing - only new refugees get queried
                ecb.AddComponent(household, new PendingRefugeeProcess
                {
                    NeedsPropertySeekerDisable = true
                });

                // Permanent marker for budget tracking - NEVER removed
                ecb.AddComponent(household, new RefugeeHousehold());

                // Relocation gate marker — presence ⟺ needs relocation. Add it only for
                // border spawns (the refugee waits at the border for a park). A park spawn
                // lands in a live park already, so it carries no marker and the migration
                // gate stays closed for it. RefugeeMigrationSystem removes the marker once
                // it relocates a border refugee into a park.
                if (spawnAtBorder)
                    ecb.AddComponent(household, new NeedsRefugeeRelocation());

                // ECB remaps the deferred household entity at playback
                if (spawnPointHasRenterBuffer)
                    ecb.AppendToBuffer(spawnPoint, new Game.Buildings.Renter { m_Renter = household });

                spawned++;
            }

            // BUG-R-015 FIX: Use Debug for frequent internal tracking logs
            if (Log.IsDebugEnabled) Log.Debug($"Queued {spawned} refugee households at {(spawnAtBorder ? "border" : "park")}");

            return spawned;
        }

        /// <summary>
        /// Get spawn point entity.
        /// </summary>
        private Entity GetSpawnPoint(bool spawnAtBorder)
        {
            if (spawnAtBorder)
            {
                var connections = m_OutsideConnectionQuery.ToEntityArray(Allocator.Temp);
                Entity result = connections.Length > 0 ? connections[m_Random.NextInt(connections.Length)] : Entity.Null;
                if (connections.IsCreated) connections.Dispose();
                return result;
            }
            else
            {
                var parks = m_ParkQuery.ToEntityArray(Allocator.Temp);
                Entity result = parks.Length > 0 ? parks[m_Random.NextInt(parks.Length)] : Entity.Null;
                if (parks.IsCreated) parks.Dispose();
                return result;
            }
        }

        // ===== Serialization =====

        /// <summary>
        /// Serialize Random state for save/load continuity.
        /// Initial session seed is intentionally non-deterministic; serialized state preserves in-flight sequence continuity.
        /// </summary>
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var state = new RefugeeSpawnServicePersistState(m_Random.state);
            RefugeeSpawnServiceCodec.Write(state, writer);
        }

        /// <summary>
        /// Deserialize Random state.
        /// </summary>
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            try
            {
                if (!m_Initialized)
                    Initialize();

                RefugeeSpawnServiceCodec.Read(reader, out var state);
                RestoreRandomState(state.RandomState);
            }
            catch (System.Exception ex)
            {
                m_Random = default;
                m_Random.state = 1u;
                Log.Warn($"Deserialize failed, using default Random state=1: {ex}");
            }
        }

        public void RestoreRandomState(uint randomState)
        {
            if (!m_Initialized)
                Initialize();

            m_Random = default;
            m_Random.state = randomState == 0u ? 1u : randomState;
            if (Log.IsDebugEnabled) Log.Debug($"Random state restored: {m_Random.state}");
        }

        /// <summary>
        /// Skip serialized SpawnService data in the reader stream without constructing a service.
        /// Must be kept in sync with Serialize — co-located here so they always match.
        /// </summary>
        public static void Skip<TReader>(TReader reader) where TReader : IReader
        {
            RefugeeSpawnServiceCodec.Skip(reader);
        }

        // ===== IDisposable =====

        private bool m_Disposed;

        /// <summary>
        /// EQ-004 FIX: Dispose queries.
        /// EntityManager-created queries hold references until disposed or world teardown.
        /// </summary>
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            if (m_Initialized)
            {
                m_ParkQuery.Dispose();
                m_OutsideConnectionQuery.Dispose();
            }

            m_Initialized = false;

            Log.Debug("Disposed");
        }
    }
}
