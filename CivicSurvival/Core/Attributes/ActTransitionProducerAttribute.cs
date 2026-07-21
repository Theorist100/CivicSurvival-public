using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks the single system that owns the authoritative fresh Act transition pipeline:
    /// it writes <c>CurrentActSingleton</c>, advances <c>ActEpochClock</c>, and
    /// publishes fresh-transition <c>ActChangedEvent</c> notifications.
    /// CIVIC480 allowlist exception: the publication is not a consumer simulation
    /// gate; analyzer skips this type entirely.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActTransitionProducerAttribute : Attribute
    {
        public string Reason { get; }

        public ActTransitionProducerAttribute(string reason)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }
}
