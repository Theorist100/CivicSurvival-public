using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a local EntityQuery structural-order cursor.
    /// The field must be paired with reads of
    /// <c>EntityQuery.GetCombinedComponentOrderVersion()</c> (or its buffer
    /// equivalent) and used only to invalidate a local cache when the
    /// archetype set under that query changes structurally.
    ///
    /// This is **not** a snapshot payload version — it carries no published
    /// data, so the A12 VersionedView contract does not apply. The reason
    /// string must name the cache that this cursor invalidates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EntityQueryOrderCursorAttribute : Attribute
    {
        public EntityQueryOrderCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
