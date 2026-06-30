using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    public enum AAPlacementMode : byte
    {
        Paid = 0,
        Heritage = 1,
        DonorCredit = 2
    }

    /// <summary>
    /// Singleton signal: AA placement tool is active.
    /// Created by AirDefenseUISystem.OnPlaceAABuilding.
    /// AAInstallationDetectorSystem consumes it when a matching Created building appears;
    /// AAPlacementLifecycleSystem owns cancellation and post-load cleanup.
    ///
    /// Lifecycle:
    /// 1. UI activates placement tool → singleton created with target prefab reference
    /// 2. AAInstallationDetectorSystem matches Created entities to the pending prefab
    /// 3. AAPlacementLifecycleSystem observes tool cancellation when no Created entity wins
    /// 4. Terminal result is emitted by lifecycle, detector reject, or pipeline apply/reject
    /// </summary>
    public struct AAPlacementPending : IComponentData
    {
        /// <summary>Prefab entity Index the player chose to place (Axiom 11: no Entity fields)</summary>
        public int PrefabIndex;

        /// <summary>Prefab entity Version</summary>
        public int PrefabVersion;

        /// <summary>Player intent selected in the UI.</summary>
        public AAPlacementMode Mode;

        /// <summary>Bridge request id that must be completed by detector or placement pipeline.</summary>
        public int RequestId;

        /// <summary>Unity frame when the placement request became pending.</summary>
        public int StartedFrame;

        /// <summary>First Unity frame where the tool was observed back at Default with no matching Created entity.</summary>
        public int ToolDefaultSinceFrame;
    }
}
