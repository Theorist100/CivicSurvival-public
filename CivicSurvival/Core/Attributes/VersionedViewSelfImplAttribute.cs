using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field that backs an explicit
    /// <c>IVersionedView&lt;T&gt;</c> implementation on the enclosing class
    /// (typically a system whose snapshot is backed by native collections
    /// such as a double-buffered <c>NativeList&lt;T&gt;</c>, so it cannot
    /// delegate to the managed <c>VersionedView&lt;T&gt;</c> primitive).
    /// The field is the producer's own atomic version counter, paired with
    /// the same threading guarantees the primitive provides.
    ///
    /// The reason string must name the snapshot type and explain why the
    /// managed primitive is unsuitable (typically: native-collection
    /// backing storage).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VersionedViewSelfImplAttribute : Attribute
    {
        public VersionedViewSelfImplAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
