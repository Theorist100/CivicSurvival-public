using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Placement
{
    /// <summary>
    /// Singleton signal: a generic building-placement tool is active. Created
    /// synchronously by <c>BuildingPlacementCommandSystem.TryActivatePlacement</c>
    /// on the UI thread so placement works while the simulation is paused
    /// (Axiom 14).
    ///
    /// Lifecycle:
    /// 1. UI activates the placement tool → singleton created with the target prefab
    ///    reference and the placement <see cref="Kind"/> + <see cref="Mode"/>.
    /// 2. <c>BuildingPlacementDetectorSystem</c> matches Created entities to the
    ///    pending prefab and dispatches resolution to the kind's handler.
    /// 3. <c>BuildingPlacementLifecycleSystem</c> observes tool cancellation when no
    ///    matching Created entity wins, and clears the transient singleton post-load.
    ///
    /// Transient — intentionally NOT serialized. A pending placement never survives a
    /// save (the lifecycle system drops it in <c>ValidateAfterLoad</c>).
    /// </summary>
    public struct BuildingPlacementPending : IComponentData
    {
        /// <summary>Prefab entity Index the player chose to place (Axiom 11: no Entity fields).</summary>
        public int PrefabIndex;

        /// <summary>Prefab entity Version.</summary>
        public int PrefabVersion;

        /// <summary>Which building kind is being placed — selects the handler.</summary>
        public BuildingPlacementKind Kind;

        /// <summary>Handler-interpreted placement mode selected in the UI (e.g. paid vs credit).</summary>
        public byte Mode;

        /// <summary>Bridge request id that must be completed by the detector or placement pipeline.</summary>
        public int RequestId;

        /// <summary>Unity frame when the placement request became pending.</summary>
        public int StartedFrame;

        /// <summary>First Unity frame where the tool was observed back at Default with no matching Created entity.</summary>
        public int ToolDefaultSinceFrame;
    }
}
