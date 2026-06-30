using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.AirDefense
{
    /// <summary>
    /// State of a visible Patriot interceptor missile — a RENDER-ONLY layer over the existing
    /// intercept formula. The missile never arbitrates HIT/MISS (that stays with
    /// <c>AALogic.CalculateInterceptChance</c> + <c>SerializableRandom</c>); it only flies toward
    /// the engaged threat so the player sees a physical interception. This keeps the balance model
    /// intact and is PvP-safe (truth = server-recompute of the formula, render is local).
    ///
    /// <para><b>Chase fields</b> (<see cref="ThreatIndex"/>/<see cref="ThreatVersion"/>,
    /// <see cref="TargetPos"/>, <see cref="Speed"/>) are populated at spawn and consumed by the
    /// movement system (Phase 2). <see cref="LaunchElapsedTime"/> drives the lifetime despawn.</para>
    ///
    /// Not serialized (no <c>ISerializable</c>): the missile is transient, so on load this component
    /// is dropped and <see cref="InterceptorTag"/> (which IS serialized as empty) lets the cleanup
    /// system despawn the orphaned render shell. A save mid-flight loses the missile, not the threat.
    /// </summary>
    public struct Interceptor : IComponentData
    {
        /// <summary>Launch point — the firing AA installation's position.</summary>
        public float3 SpawnPos;

        /// <summary>Engaged threat position snapshotted at fire time (initial aim / fallback).</summary>
        public float3 TargetPos;

        /// <summary>Chased threat entity ref (Axiom 11 — Index+Version, never a stored Entity).</summary>
        public int ThreatIndex;

        /// <summary>Chased threat entity version (Axiom 11).</summary>
        public int ThreatVersion;

        /// <summary>Missile cruise speed (m/s).</summary>
        public float Speed;

        /// <summary>Smoothed heading, persisted between ticks. The missile rotates this toward the
        /// target by a capped angle per tick (proportional-navigation feel — an arc, not an instant
        /// snap), like the Shahed's CurrentDirection. Zero on spawn → first tick aims straight.</summary>
        public float3 CurrentDirection;

        /// <summary>Latest world position, written by InterceptorMovementSystem each movement tick (and
        /// seeded to <see cref="SpawnPos"/> at spawn). Read by InterceptorExhaustSystem as the VFX seed
        /// position INSTEAD of <c>Game.Objects.Transform</c>: a Transform RO query in GameSimulation
        /// drains the city-wide transform job chain (a sync point that dominated the exhaust controller);
        /// this mod-only field is sync-free (mirror of <c>ThreatPosition</c> for ballistics). Seed only —
        /// EffectTransformSystem overwrites the VFX from the missile's InterpolatedTransform within a
        /// frame. Transient with the rest of the component (stripped/purged on load).</summary>
        public float3 CurrentPosition;

        /// <summary>Render rotation computed by InterceptorMovementSystem on the main thread each tick
        /// (look-rotation along the smoothed heading). Applied to <c>Game.Objects.Transform</c> +
        /// <c>TransformFrame</c> by the Burst <c>InterceptorRenderWriteJob</c> instead of the main thread
        /// — writing those vanilla render components from the main-thread query forced a
        /// CompleteDependencyBeforeRW universal sync every tick. Transient with the rest of the component
        /// (stripped/purged on load).</summary>
        public quaternion RenderRotation;

        /// <summary>Render velocity computed by InterceptorMovementSystem on the main thread each tick
        /// (heading × speed, zero on arrival). Applied to <c>Moving</c> + <c>TransformFrame</c> by the
        /// Burst <c>InterceptorRenderWriteJob</c> — see <see cref="RenderRotation"/>. Transient.</summary>
        public float3 RenderVelocity;

        /// <summary>Which AA fired it (always <see cref="AAType.PatriotSAM"/> for now).</summary>
        public AAType Source;

        /// <summary>Sim frame (<c>SimulationSystem.frameIndex</c>) at launch. Drives the lifetime
        /// despawn measured in sim frames — frameIndex is frozen in pause (the sim does not tick), so a
        /// paused missile does not age out while it is visually frozen (pause-safe, Axiom 14). Session-
        /// local but never compared across a load boundary (the missile is purged on load).</summary>
        public uint LaunchFrame;

        /// <summary>Source threat's generation (ThreatGenerationClock.Current at spawn). Carried on
        /// the missile so the resolution triggers build the intercept terminal outcome WITHOUT a
        /// main-thread RO read of Shahed/Ballistic (which would drain the in-flight TMS Burst jobs —
        /// perf H1). For a live intercept this equals the threat's own generation.</summary>
        public int ThreatGeneration;

        /// <summary>True if the engaged threat is a ballistic missile (vs Shahed drone). Carried so
        /// the resolution triggers pick the right intercept-state component + outcome Kind without a
        /// Shahed/Ballistic lookup.</summary>
        public bool IsBallistic;

        /// <summary>Set true by InterceptorMovementSystem the tick the missile reaches its target
        /// (dist &lt;= step). Read by InterceptorCleanupSystem (Modification4, render-safe phase) to
        /// despawn the missile immediately instead of waiting out the lifetime timer. Not serialized
        /// (the whole Interceptor component is transient — stripped on load, shell purged by
        /// InterceptorLoadPurgeSystem).</summary>
        public bool HasReachedTarget;
    }

    /// <summary>
    /// Empty marker for interceptor render entities — queried by spawn/cleanup. Implements
    /// <c>IEmptySerializable</c> so the tag survives save/load (an unserialized tag is stripped and
    /// the type disappears from the restored entity); the cleanup system reads it on load to despawn
    /// transient missiles. See memory colossal_enableable_tags_not_serialized.
    /// </summary>
    public struct InterceptorTag : IComponentData, IEmptySerializable
    {
    }

    /// <summary>
    /// Off-barrier spawn intent for an interceptor missile — mirror of <c>ThreatSpawnIntent</c>.
    /// The producer (<c>InterceptorSpawnSystem</c>, GameSimulation) records the fully-resolved spawn
    /// data here; the consumer (<c>InterceptorSpawnApplySystem</c>, Modification4) does the actual
    /// <c>CreateEntity</c> from the render-safe phase, where vanilla's render batch pipeline expects
    /// the archetype migration.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct InterceptorSpawnIntent : IBufferElementData
    {
        public float3 SpawnPos;
        public quaternion Rotation;
        public float3 TargetPos;
        public int ThreatIndex;
        public int ThreatVersion;
        public float Speed;
        public AAType Source;
        public int PrefabIndex;
        public int PrefabVersion;
        public ushort PseudoSeed;
        public byte SubMeshCount;
        public uint LaunchFrame;
        public int ThreatGeneration;
        public bool IsBallistic;
    }

    /// <summary>
    /// Singleton host carrying the <see cref="InterceptorSpawnIntent"/> buffer. Deliberately not
    /// serializable (mirror of <c>ThreatSpawnIntentHost</c>): the buffer is stripped on save/load,
    /// and a save in the producer→consumer gap simply drops the un-spawned missile (a visual, no
    /// gameplay state). <see cref="EnsureExists"/> recreates the empty host on start/load.
    /// </summary>
    public struct InterceptorSpawnIntentHost : IComponentData
    {
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, default(InterceptorSpawnIntentHost), new EnsureSingletonPolicy<InterceptorSpawnIntentHost>
            {
                EnsureShape = EnsureBuffer
            });
        }

        private static void EnsureBuffer(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<InterceptorSpawnIntent>(entity))
                em.AddBuffer<InterceptorSpawnIntent>(entity);
        }
    }
}
