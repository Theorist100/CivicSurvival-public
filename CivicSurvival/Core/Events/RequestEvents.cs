using CivicSurvival.Core.Interfaces.Core;

namespace CivicSurvival.Core.Events
{
    /// <summary>
    /// Published when <c>CommandRequestCleanupSystem</c> destroys an orphaned command request
    /// entity — either a retained input that outlived its TTL (consumer gated/disabled/skipped)
    /// or a reconciled outcome that detached from its request. Lets telemetry track whether a
    /// given producer/consumer pair leaks command entities across sessions.
    /// </summary>
    /// <param name="RequestType">Request component type name that was destroyed.</param>
    /// <param name="AgeSeconds">How long the entity survived before cleanup, in seconds.</param>
    /// <param name="Reason"><c>ttl_expired</c> or <c>outcome_orphan</c>.</param>
    public record CommandRequestOrphanedEvent(string RequestType, float AgeSeconds, string Reason) : IGameEvent;
}
