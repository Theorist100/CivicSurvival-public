using CivicSurvival.Core.Attributes;
using Unity.Entities;

namespace CivicSurvival.Core.Interfaces.Services
{
    /// <summary>
    /// Cross-system frame-scoped dedup for threat/tracer lifecycle deletion intents.
    /// Sibling systems cannot see each other's deferred ECB tags before barrier
    /// playback, so producers must claim the entity here before queueing Deleted.
    ///
    /// Lifetime is strictly frame-local: entries must not survive pause, load,
    /// new-game, or world transitions. Load-boundary systems clear the service
    /// before transient threat cleanup so recycled Entity index/version pairs
    /// cannot suppress a fresh teardown intent.
    /// </summary>
    [InfrastructureService]
    public interface IThreatLifecycleDedup
    {
        bool TryQueueDeleted(Entity entity);
        void Clear();
        int Count { get; }
    }
}
