using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// Marks a snapshot-struct field whose value is meaningful ONLY when its companion presence
    /// flag is set. A direct read that skips the flag silently yields the zero-initialised value
    /// (e.g. 0 MW, an empty fleet) instead of the archetype fallback the consumer must use, so the
    /// gated value must never be reachable except through a <c>TryGet*</c> accessor on the declaring
    /// type that returns the flag alongside the value.
    /// </summary>
    /// <remarks>
    /// CIVIC514 enforces the safe shape: a <see cref="PresenceGatedAttribute"/> field must be declared
    /// non-public (private/internal), making the unsafe direct public read impossible by accessibility
    /// and mandating the <c>TryGet*</c> form for every current and future presence-gated snapshot.
    /// <paramref name="flagName"/> names the companion flag (pass it via <c>nameof</c>) so the gating
    /// relationship is self-documenting at the field.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PresenceGatedAttribute : Attribute
    {
        /// <summary>Name of the companion presence flag that gates this field's value.</summary>
        public string FlagName { get; }

        public PresenceGatedAttribute(string flagName) => FlagName = flagName;
    }
}
