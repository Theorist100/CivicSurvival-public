using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a runtime-input dirty cursor.
    /// The field tracks changes to a non-snapshot input feeding another
    /// publisher; the published snapshot itself must still use VersionedView.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RuntimeInputDirtyCursorAttribute : Attribute
    {
        public RuntimeInputDirtyCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
