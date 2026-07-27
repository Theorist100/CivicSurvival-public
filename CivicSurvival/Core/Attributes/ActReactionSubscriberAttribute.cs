using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a system as a legitimate subscriber to <c>ActChangedEvent</c> for
    /// one-shot transition side effects (telemetry, tutorial, narrative, scheduled
    /// reactions).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActReactionSubscriberAttribute : Attribute
    {
        public string Reason { get; }

        public ActReactionSubscriberAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
