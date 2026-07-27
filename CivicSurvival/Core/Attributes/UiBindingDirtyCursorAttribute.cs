using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a UI binding dirty cursor.
    /// The field lets a binding consumer observe local UI-state changes and
    /// does not mirror a producer snapshot generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UiBindingDirtyCursorAttribute : Attribute
    {
        public UiBindingDirtyCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
