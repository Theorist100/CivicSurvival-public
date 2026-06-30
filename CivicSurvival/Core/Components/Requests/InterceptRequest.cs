using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using Unity.Entities;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using Unity.Mathematics;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Unified intercept request for all threat types.
    /// Ephemeral entity pattern - created by AirDefense/BallisticDefense, processed by InterceptProcessingSystem.
    ///
    /// Flow:
    /// 1. AirDefenseSystem/BallisticDefenseSystem creates InterceptRequest
    /// 2. InterceptProcessingSystem counts it (InterceptStatsSingleton++)
    /// 3. ThreatMovementSystem handles debris spawn (for Shaheds)
    /// 4. Request entity destroyed after processing
    ///
    /// NOTE: ThreatEntity stored as Index+Version (not Entity) to avoid vanilla
    /// SubElementDeleteSystem orphan detection that causes homeless spike.
    /// See BUG_HOMELESS_SPIKE.md for details.
    ///
    /// This request is intentionally not serialized. Index+Version references are
    /// valid only inside the current ECS world and must not survive save/load.
    /// </summary>
    // In-frame command: entity Index+Version refs are invalid across save/load BY
    // DESIGN, so faithful serialization would only persist dangling refs.
    // IEmptySerializable is the correct contract (no persistable payload); its single
    // consumer InterceptProcessingSystem.ValidateAfterLoad (IPostLoadValidation)
    // destroys all stale InterceptRequest entities on load (doctrine Invariant 3).
    // RequestPersistence + IEmptySerializable declare the explicit save-safe contract.
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(InterceptProcessingSystem))]
    public struct InterceptRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>The threat entity Index (use with ThreatEntityVersion to reconstruct Entity).</summary>
        public int ThreatEntityIndex;

        /// <summary>The threat entity Version (use with ThreatEntityIndex to reconstruct Entity).</summary>
        public int ThreatEntityVersion;

        /// <summary>Position where intercept occurred (for debris spawn).</summary>
        public float3 Position;

        /// <summary>Whether this is a ballistic missile (vs Shahed drone).</summary>
        public bool IsBallistic;

        /// <summary>
        /// Whether this request has been processed.
        /// Ensures idempotency when SimulationSystemGroup runs multiple ticks per frame.
        /// </summary>
        public bool Consumed;

        /// <summary>
        /// Reconstructs the Entity from stored Index+Version.
        /// IMPORTANT: Always check EntityManager.Exists() before using - entity may have been destroyed.
        /// </summary>
        public Entity GetThreatEntity()
        {
            return new Entity { Index = ThreatEntityIndex, Version = ThreatEntityVersion };
        }

        public void SetDefaults() => this = default;

        // IEmptySerializable marker: in-frame command; entity Index+Version
        // references are invalid across save/load — no persisted payload.
    }
}
