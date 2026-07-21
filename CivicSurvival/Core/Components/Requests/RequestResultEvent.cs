using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Terminal result event for a UI request. Created once by processors, then reported and cleaned up.
    ///
    /// Session-scoped, NOT load-bearing: it is a one-shot UI notification. The
    /// durable consequence of the request (hero deployed, budget changed, …)
    /// lives in domain state, not in this toast. <see cref="IEmptySerializable"/>
    /// (no persisted payload) + RequestResultCleanupSystem.PurgeAfterLoad purges
    /// every instance on load: a stale terminal result surviving a save would be
    /// re-collected by RequestResultCollectorSystem and republish an old reject
    /// toast against a UI request that no longer exists (W2 register row 171,
    /// requestresultevent-stale-ttl). The TTL is ElapsedTime-based and only valid
    /// in-session — the purge is what makes it correct across load.
    /// </summary>
    public struct RequestResultEvent : IComponentData, IEmptySerializable
    {
        public int RequestId;
        public RequestKind Kind;
        public RequestStatus Status;
        public FixedString64Bytes ReasonId;
        public FixedString64Bytes CanonicalEcho;
        public FixedString32Bytes DiscriminatorKind;
        public FixedString64Bytes DiscriminatorValue;
        public double CreatedTime;

        public void SetDefaults() => this = default;
    }
}
