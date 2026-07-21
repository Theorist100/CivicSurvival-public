using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as a local request-token generation cursor.
    /// The field disambiguates stale command/request completions and does not
    /// publish or mirror a snapshot payload version.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RequestTokenGenerationAttribute : Attribute
    {
        public RequestTokenGenerationAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
