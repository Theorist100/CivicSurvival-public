using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a local service-lookup refresh cursor.
    /// The field observes a provider's structural version to refresh lookups
    /// before synchronous service reads; it is not a snapshot payload cache.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ServiceLookupRefreshCursorAttribute : Attribute
    {
        public ServiceLookupRefreshCursorAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
