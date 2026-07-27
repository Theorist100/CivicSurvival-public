using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a dirty cursor for an ECS-owned projection.
    /// The field changes when owner ECS state relevant to a reader changes;
    /// it is not a cached snapshot payload generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EcsProjectionDirtyCursorAttribute : Attribute
    {
        public EcsProjectionDirtyCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
