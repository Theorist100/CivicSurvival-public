using System;
using CivicSurvival.Core.Attributes;
using Unity.Entities;
using Unity.Jobs;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Scope mask for <see cref="IRenderWriteBarrier"/>.
    ///
    /// <para><strong>SCOPE: entity-class × component.</strong> Each value names a specific
    /// entity class (threat, tracer, building, …) combined with a vanilla render component.
    /// Different entity classes occupy different ECS archetypes (disjoint chunks), so
    /// job writes to <c>DroneRenderWriteJob</c>-owned chunks cannot race with main-thread
    /// reads of building chunks. The mask encodes that disjointness explicitly so
    /// <see cref="IRenderWriteBarrier.Consume"/> waits only for the specific producers
    /// that actually write the entities a consumer reads.</para>
    ///
    /// <para><strong>Zero-producer flag is valid.</strong> A flag with no current
    /// producers is a legitimate scope declaration: <see cref="Consume"/> on it costs
    /// nothing (no handles → no <c>Complete()</c>), while enforcing that any future
    /// out-of-band producer of that entity class is declared here rather than bypassing
    /// the barrier. Adding a new producer means publishing under the matching flag; the
    /// consumer's existing <c>Consume</c> call automatically covers it.</para>
    ///
    /// <para><strong>Adding a new out-of-band job</strong> (one whose handle is intentionally
    /// kept out of <c>system.Dependency</c> to avoid contaminating the ECS per-type
    /// dependency chain): declare a flag for its entity-class × component, call
    /// <see cref="IRenderWriteBarrier.Publish"/> after scheduling, and update the XML-doc
    /// of the new flag with the producer name.</para>
    /// </summary>
    [Flags]
    public enum RenderWriteComponentMask : uint
    {
        None = 0,

        /// <summary>
        /// <c>Game.Objects.Transform</c> on threat entities
        /// (<c>ThreatPosition</c> archetype — drones and ballistic missiles).
        /// Producer: <c>ThreatMovementSystem.DroneRenderWriteJob</c> (Burst parallel,
        /// scheduled on <c>m_RenderJobHandle</c>, handle kept outside <c>TMS.Dependency</c>
        /// to avoid contaminating the ECS Transform dependency chain).
        /// </summary>
        ThreatTransform = 1 << 0,

        /// <summary>
        /// <c>Moving</c> on threat entities (<c>ThreatPosition</c> archetype).
        /// Producer: <c>ThreatMovementSystem.DroneRenderWriteJob</c>.
        /// </summary>
        ThreatMoving = 1 << 1,

        /// <summary>
        /// <c>TransformFrame</c> buffer on threat entities
        /// (<c>ThreatPosition</c> archetype).
        /// Producer: <c>ThreatMovementSystem.DroneRenderWriteJob</c>.
        /// </summary>
        ThreatTransformFrame = 1 << 2,

        /// <summary>
        /// <c>Game.Objects.Transform</c> on building entities (static AA props and
        /// damage-target buildings). Zero current civic out-of-band producers — this flag
        /// exists so that main-thread Transform reads on buildings are always routed through
        /// the barrier. If a future civic job writes Transform on buildings outside
        /// <c>system.Dependency</c>, it must publish under this flag; existing
        /// <see cref="IRenderWriteBarrier.Consume"/> call sites cover it automatically.
        /// Because there are no producers today, <c>Consume(BuildingTransform)</c> completes
        /// instantly (zero handles → no <c>Complete()</c> call).
        /// </summary>
        BuildingTransform = 1 << 3,

        /// <summary>
        /// <c>Game.Objects.Transform</c> on interceptor missile entities
        /// (<c>InterceptorTag</c> archetype — Patriot SAM render shells). Disjoint archetype
        /// from threats: an interceptor consumer must NOT wait on the drone producer and
        /// vice-versa, so this is a separate flag from <see cref="ThreatTransform"/>.
        /// Producer: <c>InterceptorMovementSystem.InterceptorRenderWriteJob</c> (Burst parallel,
        /// scheduled on <c>m_RenderJobHandle</c>, handle kept outside the system's
        /// <c>Dependency</c> to avoid contaminating the ECS Transform dependency chain).
        /// </summary>
        InterceptorTransform = 1 << 4,

        /// <summary>
        /// <c>Moving</c> on interceptor missile entities (<c>InterceptorTag</c> archetype).
        /// Producer: <c>InterceptorMovementSystem.InterceptorRenderWriteJob</c>.
        /// </summary>
        InterceptorMoving = 1 << 5,

        /// <summary>
        /// <c>TransformFrame</c> buffer on interceptor missile entities
        /// (<c>InterceptorTag</c> archetype).
        /// Producer: <c>InterceptorMovementSystem.InterceptorRenderWriteJob</c>.
        /// </summary>
        InterceptorTransformFrame = 1 << 6,

        /// <summary>
        /// Convenience: all three interceptor render components written by
        /// <c>InterceptorMovementSystem.InterceptorRenderWriteJob</c>. Published by the producer and
        /// consumed by InterceptorCleanupSystem / InterceptorSpawnApplySystem before their Modification4
        /// structural ops — one source for the producer + both consumers.
        /// </summary>
        InterceptorRender = InterceptorTransform | InterceptorMoving | InterceptorTransformFrame,
    }

    /// <summary>
    /// Scope mask for <see cref="IVanillaWriteBarrier"/>.
    ///
    /// **SCOPE: per-COMPONENT.** Each value names a vanilla-written component type.
    /// Unity ECS dependency fences are per-component-type — there is no field-level
    /// fence — so a ticket draining the write fence for component <c>T</c> also covers
    /// every field on <c>T</c> that any vanilla writer writes.
    ///
    /// Example: <see cref="VanillaWriteComponentMask.ElectricityProducer"/> covers BOTH
    /// <c>ElectricityProducer.m_Capacity</c> AND <c>ElectricityProducer.m_LastProduction</c>.
    /// Vanilla <c>PowerPlantAISystem.PowerPlantTickJob</c> writes both fields on the same
    /// RW pass (single line in the decompile); readers must consume the ticket before any
    /// field read (including <c>.TryGetComponent</c> or <c>[entity]</c> indexer access on
    /// the lookup).
    ///
    /// <c>RegisterAfter</c> / <c>RegisterBefore</c> orders system invocation only. It
    /// does NOT insert a job fence. Vanilla parallel jobs survive into the next system's
    /// body unless that system explicitly drains the fence — consume the ticket here, or
    /// call <c>EntityManager.SetComponentData</c> which drains. <c>ComponentLookup&lt;T&gt;</c>
    /// access does NOT drain the fence on its own.
    ///
    /// When extending this enum, name the COMPONENT, not the field. Document every vanilla
    /// writer of that component so future consumers know which job's writes are being
    /// awaited.
    /// </summary>
    [Flags]
    public enum VanillaWriteComponentMask : uint
    {
        None = 0,
        /// <summary>
        /// Covers <c>Game.Buildings.ElectricityProducer</c> as a whole component, including
        /// both <c>m_Capacity</c> and <c>m_LastProduction</c>. Vanilla writer:
        /// <c>Game.Simulation.PowerPlantAISystem.PowerPlantTickJob</c> (writes both fields
        /// on the same RW pass; <c>PowerPlantAISystem.cs:247</c> in the decompile, RW
        /// handle declared <c>:362</c>). Update interval: 128 frames. No other vanilla
        /// writer of this component has been observed in <c>D:\_decompiled\Game\</c>.
        /// </summary>
        ElectricityProducer = 1 << 0

        // NOTE: Efficiency (1 << 1) and ResourceConsumer (1 << 2) were retired 2026-06-12.
        // Their only consumer was PowerCapacityResolverSystem's main-thread per-tick drain —
        // measured at 78% of that system's 13 ms/throttled-tick cost (SP:PCR.* split; the RW
        // Efficiency drain alone 7.99 ms/call, waiting on ~40 vanilla reader systems). The
        // resolver now touches Efficiency/ResourceConsumer ONLY inside PlantResolveJob, ordered
        // by the job graph (scheduled on Dependency). Re-adding a mask here means re-adding a
        // main-thread CompleteDependency drain — measure first.
    }

    /// <summary>
    /// Producer-to-reader barrier for same-frame render component writes.
    /// Consumers get no raw JobHandle; the ticket returned by Consume is the
    /// proof that matching producers have been waited before render reads.
    /// </summary>
    [InfrastructureService]
    public interface IRenderWriteBarrier
    {
        void Publish(JobHandle handle, Type producer, RenderWriteComponentMask mask);

        RenderWriteTicket Consume(Type reader, RenderWriteComponentMask mask);
    }

    public readonly ref struct RenderWriteTicket
    {
        internal RenderWriteTicket(RenderWriteComponentMask consumed)
        {
            Consumed = consumed;
        }

        public RenderWriteComponentMask Consumed { get; }

        public bool Covers(RenderWriteComponentMask mask)
            => mask != RenderWriteComponentMask.None
               && (Consumed & mask) == mask;
    }

    /// <summary>
    /// Producer-to-reader barrier for vanilla job writes whose raw JobHandle is
    /// not exposed to the mod. Consumers still get a ticket instead of reaching
    /// directly for the component field.
    /// </summary>
    [InfrastructureService]
    public interface IVanillaWriteBarrier
    {
        VanillaWriteTicket Consume(
            EntityManager entityManager,
            Type reader,
            VanillaWriteComponentMask mask);
    }

    public readonly ref struct VanillaWriteTicket
    {
        internal VanillaWriteTicket(VanillaWriteComponentMask consumed)
        {
            Consumed = consumed;
        }

        public VanillaWriteComponentMask Consumed { get; }

        public bool Covers(VanillaWriteComponentMask mask)
            => mask != VanillaWriteComponentMask.None
               && (Consumed & mask) == mask;
    }
}
