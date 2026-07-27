using System.Collections.Generic;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Runtime side-set of grid plants that <c>ConstructionDelaySystem</c> has classified
    /// (created a sidecar for, or recorded as pre-existing/instant-on), keyed by the plant's
    /// <b>stable building Index</b> (<see cref="StablePlantIdentityRegistry.ClassificationKey"/>) —
    /// NOT the fragile <c>Index|Version</c> pack. The capacity resolver's construction gate reads
    /// it: until a plant is marked, the resolver leaves the add-site default so a brand-new plant
    /// resolves to 0 MW instead of leaking full nameplate for the ~1 s before CDS first runs.
    ///
    /// Why Index, not Index|Version: a live plant's structural reaction to missile damage / grid-node
    /// loss desynchronises an Index|Version key, which dropped the plant out of this set and made the
    /// resolver gate strand the still-standing plant at 0 MW (and re-classify it as new). The Index
    /// is stable for a live entity, so the mark survives churn; <see cref="Unmark"/> drops it only on
    /// a confirmed demolition (CDS prunes the building from its registry).
    ///
    /// A managed marker — NOT a component on the vanilla plant — by design. Adding a tag to the
    /// rendered building would migrate its archetype, and doing that from GameSimulation (where
    /// CDS runs) risks the vanilla render-batch Burst crash. This mirrors two established
    /// patterns: CDS keeps construction state on a separate <c>UnderConstruction</c> sidecar
    /// entity, and <c>PowerCapacityIndexSystem</c>'s A2 dirty-set replaced a structural
    /// <c>PowerCapacityIndexDirty</c> tag on the plant for exactly this reason.
    ///
    /// Transient (not serialized). After load CDS re-marks every current plant on its persistent
    /// registry, and the resolver bypasses the gate on the afterLoad pass — so no persistence is
    /// needed.
    ///
    /// Producer (CDS) and consumer (resolver) both run on the main thread in GameSimulation on
    /// separate ticks, so the plain <see cref="HashSet{T}"/> needs no lock (same as the index
    /// system's A2 dirty-set).
    /// </summary>
    public static class ConstructionClassifiedState
    {
        private static readonly HashSet<long> s_Classified = new();

        public static void Mark(long buildingKey) => s_Classified.Add(buildingKey);

        public static bool IsClassified(long buildingKey) => s_Classified.Contains(buildingKey);

        /// <summary>Drop a single plant's classification — called only when CDS confirms the plant
        /// was demolished (pruned from its registry), so the stale mark cannot keep a reused Index
        /// slot falsely classified.</summary>
        public static void Unmark(long buildingKey) => s_Classified.Remove(buildingKey);

        public static void Clear() => s_Classified.Clear();
    }
}
