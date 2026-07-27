using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a static registry generation cursor.
    /// The field invalidates registration/wiring validation after producer or
    /// consumer membership changes; it carries no published snapshot payload.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RegistryGenerationCursorAttribute : Attribute
    {
        public RegistryGenerationCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
