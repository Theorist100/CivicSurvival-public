using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Domain.Spotters
{
    /// <summary>
    /// Spotter countermeasures persistent state (ECS-native).
    /// Replaces SpotterCountermeasuresService internal state.
    ///
    /// Access: SystemAPI.GetSingleton&lt;SpotterCountermeasuresState&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;SpotterCountermeasuresState&gt;()
    ///
    /// Writer: SpotterAggregateSystem (sole owner via [SingletonOwner] — simulation + request processing)
    /// Readers: UI panels, stats singleton
    ///
    /// Serialized via SpotterSystem.Serialize/Deserialize
    /// Buffers attached: InternetDisabledBuffer, EvacuatedReturnBuffer
    /// </summary>
    public struct SpotterCountermeasuresState : IComponentData
    {
        /// <summary>Total SBU visits performed (for progressive pricing).</summary>
        public int TotalSBUVisits;

        /// <summary>Total evacuations performed.</summary>
        public int TotalEvacuations;

        /// <summary>Counter-OSINT operation active (daily cost deducted).</summary>
        public bool CounterOSINTActive;

        /// <summary>Random state for deterministic save/load.</summary>
        public Random RandomState;

        public static SpotterCountermeasuresState Default => new()
        {
            TotalSBUVisits = 0,
            TotalEvacuations = 0,
            CounterOSINTActive = false,
#pragma warning disable CIVIC156 // Deterministic ECS seed: re-seeded from save on deserialize
            RandomState = new Random(0x53504F54u) // "SPOT"
#pragma warning restore CIVIC156
        };

        public static void EnsureExists(EntityManager em)
        {
            var state = Default;
            state.RandomState = Random.CreateFromIndex((uint)(em.World.GetHashCode() ^ 0x53504F54));

            CivicSingleton.Ensure(em, state, new EnsureSingletonPolicy<SpotterCountermeasuresState>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<InternetDisabledBuffer>(entity))
                em.AddBuffer<InternetDisabledBuffer>(entity);
            if (!em.HasBuffer<EvacuatedReturnBuffer>(entity))
                em.AddBuffer<EvacuatedReturnBuffer>(entity);
        }
    }
}
