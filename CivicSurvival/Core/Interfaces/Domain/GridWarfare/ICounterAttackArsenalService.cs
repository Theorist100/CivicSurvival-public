using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.GridWarfare
{
    /// <summary>
    /// Cross-domain boundary to the counter-attack arsenal (Axiom 5). Implemented by
    /// <c>CounterAttackArsenalSystem</c> (GridWarfare domain), which owns the
    /// <see cref="CounterAttackArsenal"/> singleton. Consumers in other domains (the
    /// launch phase 3.0.3, a hidden factory in Phase-30b) cross through this interface
    /// instead of reading the singleton directly or forcing a job-dependency sync.
    ///
    /// Spend / replenish are synchronous main-thread state writes (no ECB delay), so the
    /// launch gate can decide pause-safely on the UI/sync path. Null-object semantics:
    /// when GridWarfare is closed or pre-load, every read returns 0/false and every
    /// mutation is a no-op (fail-closed — a launch attempted before the system exists
    /// finds no stock and is rejected).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.GridWarfareName)]
    public interface ICounterAttackArsenalService
    {
        /// <summary>True once the arsenal singleton exists and is writable.</summary>
        [NullReturn(false)]
        bool IsAvailable { get; }

        /// <summary>Current stock of the given munition kind (0 when unavailable).</summary>
        int StockOf(ArsenalKind kind);

        /// <summary>True if at least <paramref name="count"/> units of <paramref name="kind"/> are in stock.</summary>
        [NullReturn(false)]
        bool HasStock(ArsenalKind kind, int count = 1);

        /// <summary>
        /// Spend <paramref name="count"/> units of <paramref name="kind"/> if available.
        /// Returns false (and writes nothing) when stock is insufficient — stock never
        /// goes negative. Called by the launch phase (3.0.3); mirrors
        /// <c>GridOperationEligibility.CanPrepareOperation</c>'s "check then commit" shape.
        /// </summary>
        [NullReturn(false)]
        bool TrySpend(ArsenalKind kind, int count = 1);

        /// <summary>
        /// Public replenish entry point. Adds <paramref name="count"/> units of
        /// <paramref name="kind"/> to stock (clamped at a sane ceiling). Called by the
        /// paid-import / donor pipeline (channel a) and by the hidden-factory production
        /// step (channel b, Phase-30b) once their cost/production is resolved. The budget
        /// gating happens upstream in the pipeline — this only applies the granted units.
        /// </summary>
        void Replenish(ArsenalKind kind, int count);

        /// <summary>
        /// Allocate a stable non-zero procurement batch id above any in-flight batch.
        /// The owning system holds the monotonic counter so two producers (shadow import,
        /// donors) in the same frame cannot collide. Pass the result to
        /// <c>ArsenalProcurementEmitter.QueuePaidProcurement</c> /
        /// <c>QueueFreeGrant</c>. The generated null-object returns 0 (no system →
        /// no usable id; the emitter rejects it).
        /// </summary>
        long AllocateProcurementBatchId();
    }
}
