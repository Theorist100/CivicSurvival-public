using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as an async request generation cursor.
    /// The field is captured by background work so late responses can be
    /// rejected; it is not a snapshot payload version.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AsyncRequestGenerationAttribute : Attribute
    {
        public AsyncRequestGenerationAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
