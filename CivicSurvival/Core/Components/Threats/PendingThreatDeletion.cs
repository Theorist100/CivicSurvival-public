using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Render-safe deletion signal — enableable tag living in the drone render archetype
    /// (<c>ThreatSpawnApplySystem.m_DroneArchetype</c> / <c>m_BallisticArchetype</c>), added
    /// disabled at spawn. The threat-lifecycle producers
    /// (<c>ThreatTerminalizationSystem</c>, <c>ThreatDebugSystem</c>) flip the bit to enabled
    /// from GameSimulation when a drone is terminalized; the consumer
    /// (<c>ThreatDeletionApplySystem</c>, Modification4) reads the enabled bit and performs
    /// the structural <c>AddComponent&lt;Deleted&gt;</c> on the drone from THAT phase.
    ///
    /// Why a separate signal instead of the structural add in the producer: adding
    /// <c>Deleted</c> to a vanilla render drone migrates its chunk, and doing that from
    /// GameSimulation (LateUpdate, end of frame) lands the migration out of phase with the
    /// vanilla render batch pass (<c>RequiredBatchesSystem</c> / <c>PreCullingSystem</c> /
    /// <c>BatchManagerSystem</c> read a stale, zeroed render chunk-cache on the main thread and
    /// crash a vanilla Burst batch job — the render-chunk-cache crash class). Flushing the
    /// <c>Deleted</c> add in Modification4 (mirror of vanilla <c>IgniteSystem</c>) keeps the
    /// migration in phase. Setting an enableable bit is NOT a structural change (no chunk
    /// migration), so the producer can flip it from GameSimulation safely.
    ///
    /// Why a dedicated tag and not the existing <c>PendingDestruction</c>: that flag's
    /// semantics are broader — it is the generic "queued for end-of-frame cleanup" marker,
    /// not specifically "this drone is ready for the render-safe Deleted add". Reusing it would
    /// pull other lifecycle entities into the drone deletion consumer. This tag
    /// means exactly: a render drone whose enable-bit teardown the producer already committed,
    /// now awaiting its structural Deleted in the render-safe phase.
    ///
    /// Not serialized (no <c>ISerializable</c>): like <c>ActiveThreat</c> /
    /// <c>PendingDestruction</c>, this enableable tag has no serializer, so it is stripped on
    /// save/load. A save taken in the 1-frame window between producer (frame N) and consumer
    /// (frame N+1) restores the drone without the signal; <c>ThreatLoadRenderReinitSystem</c>
    /// re-adds the tag (disabled) during reinit and either resumes the in-flight threat or
    /// purges an already-terminal one — so the terminalization is neither lost nor leaked.
    /// </summary>
    public struct PendingThreatDeletion : IComponentData, IEnableableComponent { }
}
