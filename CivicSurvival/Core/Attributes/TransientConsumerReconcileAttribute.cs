using System;

namespace CivicSurvival.Core.Attributes
{
    /// <summary>
    /// How a transient-event consumer keeps its durable side-effect coherent
    /// across save/load. See SAVE_LOAD_LIFECYCLE_DOCTRINE.md Invariant 5.
    /// </summary>
    public enum ReconcileMode
    {
        /// <summary>
        /// The consumer's per-event work does not survive save/load (counts into
        /// an IEmptySerializable singleton, in-frame cleanup, deterministic
        /// orphan owner). Nothing to reconcile.
        /// </summary>
        NoDurableSideEffect = 0,

        /// <summary>
        /// The side-effect is fully reconstructable on load from a named durable
        /// state the consumer reads independently of the event. The consumer's
        /// ValidateAfterLoad rebuilds it from <see cref="TransientConsumerReconcileAttribute.DurableState"/>.
        /// </summary>
        ReconcilesFrom = 1,

        /// <summary>
        /// The operation is carried by a durable record (a domain sidecar or
        /// PendingOperation&lt;TPayload&gt;) that the transient event merely
        /// mirrors in-session. The outbox identity round-trips via
        /// KeyedSerializer.WriteEntityField — never raw Index+Version.
        /// </summary>
        OwnsDurableOutbox = 2,

        /// <summary>
        /// Losing the side-effect across the in-flight window is acceptable by
        /// design. Requires a written <see cref="TransientConsumerReconcileAttribute.Justification"/>.
        /// </summary>
        ExplicitlyLossyAndSafe = 3,
    }

    /// <summary>
    /// Declares a system's reconciliation contract for a transient event type it
    /// consumes (an <c>IComponentData + ICommandRequest + IEmptySerializable</c>
    /// one-frame signal). Required by CIVIC426 on every consumer; the mode's
    /// correctness is manual review against SAVE_LOAD_LIFECYCLE_DOCTRINE.md
    /// Invariant 5. Apply once per consumed transient event type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class TransientConsumerReconcileAttribute : Attribute
    {
        /// <summary>The transient event component type this declaration covers.</summary>
        public Type EventType { get; }

        /// <summary>The declared reconciliation mode for <see cref="EventType"/>.</summary>
        public ReconcileMode Mode { get; }

        /// <summary>
        /// Durable state the side-effect reconciles from / is carried by.
        /// Required for <see cref="ReconcileMode.ReconcilesFrom"/> and
        /// <see cref="ReconcileMode.OwnsDurableOutbox"/>; otherwise null.
        /// </summary>
        public Type? DurableState { get; set; }

        /// <summary>
        /// Written reason the loss is harmless. Required (non-empty) for
        /// <see cref="ReconcileMode.ExplicitlyLossyAndSafe"/>; otherwise null.
        /// </summary>
        public string? Justification { get; set; }

        public TransientConsumerReconcileAttribute(Type eventType, ReconcileMode mode)
        {
            EventType = eventType;
            Mode = mode;
        }
    }
}
