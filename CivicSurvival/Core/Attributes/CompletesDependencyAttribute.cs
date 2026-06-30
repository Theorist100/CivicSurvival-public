using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Documents that the annotated method (or every method on the annotated class)
    /// intentionally triggers an ECS sync point — completes a job dependency, runs
    /// <c>ToEntityArray</c>/<c>ToComponentDataArray</c>, calls
    /// <c>Dependency.Complete()</c>, <c>CalculateEntityCount</c>, etc. — and that
    /// the sync is acceptable for the stated reason.
    ///
    /// Suppresses the hot-path sync-point analyzers (CIVIC081, CIVIC185, CIVIC218,
    /// CIVIC219, CIVIC220, CIVIC089) inside the annotated scope. Restricted by CIVIC464
    /// width rule when combined with <see cref="HotPathSystemAttribute"/>: class-level
    /// <c>[CompletesDependency]</c> and broad update-method scope (<c>OnUpdate</c> /
    /// <c>OnUpdateImpl</c> / <c>OnThrottledUpdate</c>) are rejected inside a
    /// <c>[HotPathSystem]</c> class. Narrow private helpers (one-shot bulk init,
    /// off-hot-path snapshot helpers) are allowed.
    ///
    /// <para>
    /// <c>reason</c> must be a non-empty, descriptive string explaining the trade-off,
    /// e.g. <c>"throttled OnUpdate — sync amortised over N frames"</c>,
    /// <c>"one-shot OnLoadRestore"</c>, <c>"debug-only path; #if DEBUG"</c>,
    /// <c>"RequireForUpdate gate ensures query non-empty when reached"</c>. Empty
    /// or whitespace-only reasons are rejected by CIVIC464.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CompletesDependencyAttribute : Attribute
    {
        public CompletesDependencyAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
