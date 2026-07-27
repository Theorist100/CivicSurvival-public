using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a collection-returning member (array, IList&lt;T&gt;,
    /// IReadOnlyList&lt;T&gt;, IEnumerable&lt;T&gt;, ICollection&lt;T&gt;,
    /// IReadOnlyCollection&lt;T&gt;) as returning an empty collection in the
    /// generated null object. Generator emits <c>Array.Empty&lt;T&gt;()</c>
    /// for the recognised interfaces.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class NullReturnEmptyAttribute : Attribute
    {
    }
}
