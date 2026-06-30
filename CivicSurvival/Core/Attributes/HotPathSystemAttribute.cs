using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an ECS system as a high-frequency hot-path: processes 100+ entities
    /// per frame and must not introduce sync points in its update path. Elevates
    /// the sync-point analyzers (CIVIC081, CIVIC185, CIVIC218, CIVIC219, CIVIC220,
    /// CIVIC089) from Warning to Error inside the annotated class — sync = build break.
    ///
    /// <para>
    /// Restricted by CIVIC464 width rule with <see cref="CompletesDependencyAttribute"/>:
    /// class-level <c>[CompletesDependency]</c> and broad update-method scope
    /// (<c>OnUpdate</c> / <c>OnUpdateImpl</c> / <c>OnThrottledUpdate</c>) are rejected
    /// inside a <c>[HotPathSystem]</c> class — a hot-path update cannot legitimately
    /// complete dependency. Narrow private helpers carrying <c>[CompletesDependency(reason)]</c>
    /// are allowed when the sync is one-shot or off-hot-path (e.g.,
    /// <c>ThreatMovementSystem.SnapshotArrivedBallistics</c>).
    /// </para>
    ///
    /// <para>
    /// Current carriers (see <c>Docs/Reference/HIGH_FREQUENCY_ECS_SYSTEMS.md</c> §1):
    /// <c>ThreatMovementSystem</c>, <c>ThreatDamageSystem</c>,
    /// <c>AirDefenseOrchestrator</c>, <c>TracerRenderSystem</c>,
    /// <c>PowerCapacityResolverSystem</c>.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HotPathSystemAttribute : Attribute
    {
    }
}
