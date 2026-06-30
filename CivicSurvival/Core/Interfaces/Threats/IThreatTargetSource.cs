using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Threats
{
    /// <summary>
    /// Provides a pre-filtered, cached snapshot of building targets for threat
    /// spawning. The producer system rebuilds the cache on its own throttled
    /// schedule (off the wave hot path), so consumers read without ever
    /// touching <see cref="Game.Objects.Transform"/> writers.
    ///
    /// Phase 5 pattern (mirrors <see cref="IThreatArrivalSource"/>): consumer
    /// queries here have zero <c>CompleteDependencyBeforeRO</c> cost because
    /// the producer's queries live on a separate system whose update cadence
    /// is decoupled from <c>SimulationSystemGroup</c> ticks.
    ///
    /// Returned views are valid until the producer's next refresh; consumers
    /// must not retain them across frames.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.WavesName)]
    public interface IThreatTargetSource
    {
        /// <summary>True after the cache has been built at least once.</summary>
        bool IsReady { get; }

        /// <summary>Read-only view of energy targets (PowerPlant + Transformer).</summary>
        NativeArray<TargetData>.ReadOnly Energy { get; }

        /// <summary>Read-only view of critical-infrastructure targets (Hospital + WaterPump).</summary>
        NativeArray<TargetData>.ReadOnly Critical { get; }

        /// <summary>Read-only view of public-service targets (FireStation + PoliceStation).</summary>
        NativeArray<TargetData>.ReadOnly Service { get; }

        /// <summary>Read-only view of civilian targets (Residential).</summary>
        NativeArray<TargetData>.ReadOnly Civilian { get; }

        /// <summary>
        /// Unconditional rebuild before a wave reads the catalogue. Required because the
        /// producer's throttled refresh is gated off in peacetime (Calm), so the last cached
        /// snapshot may predate buildings constructed during the lull. Doubles as the cold-start
        /// build (also marks <see cref="IsReady"/>), so no separate ensure-ready entry is needed.
        /// </summary>
        void ForceRefreshForWave();
    }

    /// <summary>
    /// Cached target row consumed by <see cref="IThreatTargetSource"/>.
    /// Lifted out of <c>ThreatTargetSelector</c> so the interface contract
    /// lives in <c>Core</c> per the domain-isolation axiom.
    /// </summary>
    public struct TargetData
    {
        public Entity Entity;
        public float3 Position;
        public TargetCategory Category;

        /// <summary>
        /// True for the highest-impact target within its category — currently a power
        /// plant (<c>ElectricityProducer</c>) inside <see cref="TargetCategory.Energy"/>,
        /// as opposed to a transformer. A focus-cluster seeds on a high-value target first
        /// so a small wave demolishes a generator (deterministic deficit) rather than a
        /// transformer whose loss may reroute around a redundant grid path.
        /// </summary>
        public bool IsHighValue;

        /// <summary>
        /// Weight for weighted random selection WITHIN a category. For power plants —
        /// the residual nameplate in MW (live <c>PlantBaseCapacity</c> minus accumulated
        /// operational/disaster damage), minimum 1; for every other target —
        /// <c>Waves.NonPlantTargetWeightMW</c> from the balance config. Makes a 7500 MW
        /// nuclear plant proportionally more attractive than a 105 MW wind turbine
        /// instead of equally likely, so wave damage tracks real grid capacity.
        /// </summary>
        public int WeightMW;
    }
}
