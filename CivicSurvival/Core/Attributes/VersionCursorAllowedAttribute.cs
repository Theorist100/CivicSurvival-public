using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a version cursor that is explicitly advanced through
    /// VersionedView.Observe(ref cursor), not by hand-written Version equality.
    /// The reason must explain why the owning type cannot hide the cursor behind
    /// a narrower wrapper.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VersionCursorAllowedAttribute : Attribute
    {
        public VersionCursorAllowedAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
