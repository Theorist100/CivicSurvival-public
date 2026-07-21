using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks an integer field as one half of an entity-identity once-guard
    /// pair (<c>m_LastProcessedIndex</c> + <c>m_LastProcessedVersion</c>).
    /// The pair records the (Index, Version) of an entity already processed
    /// so the same entity is skipped on subsequent ticks. The field carries
    /// no published payload — it is identity bookkeeping, not a snapshot
    /// version. The A12 VersionedView contract does not apply.
    ///
    /// The reason string must name the paired Index field and the pending
    /// state this guard tracks (e.g. "pending AA installation").
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EntityOnceGuardAttribute : Attribute
    {
        public EntityOnceGuardAttribute(string reason)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; }
    }
}
