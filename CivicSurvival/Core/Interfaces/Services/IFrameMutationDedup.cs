using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Cross-system frame-scoped dedup state for structural mutations
    /// (Destroy / Ignite) queued against a building via ECB. Single shared
    /// instance published through <see cref="CivicSurvival.Core.Infrastructure.ServiceRegistry"/>;
    /// per-system <c>NativeHashSet&lt;Entity&gt;</c> guards (the old shape) are
    /// replaced by calls into this contract so that
    /// <c>BackupPowerEffectsSystem</c>, <c>CounterfeitBatteryFireSystem</c>,
    /// <c>PlantWearSimulation</c>/<c>PlantExplosionService</c>, and
    /// <c>ThreatDamageSystem</c> all observe each other's queued intent inside
    /// the same simulation frame.
    ///
    /// State is **frame-local**: a dedicated PostSimulation clear system
    /// (<c>FrameMutationDedupClearSystem</c>) empties the map before
    /// <c>ModCleanupBarrier</c> playback, so the next sim tick observes an
    /// empty dedup map. Not persisted across save/load; not observed by
    /// vanilla code.
    ///
    /// Keys are <c>Entity.Index</c> (int) rather than <c>Entity</c> values.
    /// Same-frame entity-version conflicts cannot happen because vanilla
    /// gameplay destroys take effect at <c>GameSimulationEndBarrier</c> before
    /// the dedup map is cleared. Cross-frame entity-version safety is delivered by
    /// the <c>HasComponent&lt;Destroyed&gt;</c> / <c>HasComponent&lt;Deleted&gt;</c>
    /// guards in <c>BuildingDamageHelper</c> — those remain authoritative.
    ///
    /// Thread-safety: all methods are intended to be called from the main
    /// thread (system <c>OnUpdate</c> bodies). The implementation does not lock
    /// internal state and Burst jobs MUST NOT call into it; that mirrors the
    /// pre-Phase-8 per-system <c>NativeHashSet&lt;Entity&gt;</c> usage which
    /// was already main-thread only.
    /// </summary>
    [InfrastructureService]
    public interface IFrameMutationDedup
    {
        /// <summary>
        /// Queue a Destroy intent for the given entity index. Returns
        /// <c>true</c> if this is the first Destroy queue this frame for the
        /// target; <c>false</c> if Destroy was already queued (caller short
        /// circuits the duplicate destroy path). Existing
        /// <see cref="FrameMutationKind.Ignite"/> on the same target does NOT
        /// block Destroy — Destroy is the strictly stronger intent and is
        /// always recorded. The resulting state is
        /// <see cref="FrameMutationKind.Both"/> when both were queued.
        /// </summary>
        bool TryQueueDestroy(int entityIndex);

        /// <summary>
        /// Queue an Ignite intent for the given entity index. Returns
        /// <c>true</c> if this is the first Ignite queue this frame for the
        /// target; <c>false</c> if Ignite was already queued OR if Destroy was
        /// already queued for the same target (never run Ignite on a building
        /// already queued for Destroy in the same frame —
        /// <c>BuildingCondition</c> writes against an entity headed for
        /// <c>Game.Objects.Destroy</c> are wasted at best and racy at worst).
        /// </summary>
        bool TryQueueIgnite(int entityIndex);

        /// <summary>
        /// Is any mutation kind currently queued for this entity index?
        /// </summary>
        bool IsQueued(int entityIndex);

        /// <summary>
        /// Return the bit-flag set of kinds currently queued for this entity
        /// index (zero / <see cref="FrameMutationKind.None"/> if none).
        /// </summary>
        FrameMutationKind GetQueuedKind(int entityIndex);

        /// <summary>
        /// Drop all queued intents. Called by
        /// <c>FrameMutationDedupClearSystem</c> in PostSimulation before
        /// <c>ModCleanupBarrier</c> playback. Tests may also call this
        /// directly to reset state between cases.
        /// </summary>
        void Clear();

        /// <summary>
        /// Current number of distinct entity indices with at least one queued
        /// intent. Exposed for diagnostics and test asserts only.
        /// </summary>
        int Count { get; }
    }
}
