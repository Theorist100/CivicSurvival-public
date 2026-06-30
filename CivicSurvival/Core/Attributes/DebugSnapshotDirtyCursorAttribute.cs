using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a debug-only snapshot dirty cursor.
    /// The field may gate developer diagnostics under DEBUG and must not be
    /// used as a production snapshot payload cache exemption.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DebugSnapshotDirtyCursorAttribute : Attribute
    {
        public DebugSnapshotDirtyCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
