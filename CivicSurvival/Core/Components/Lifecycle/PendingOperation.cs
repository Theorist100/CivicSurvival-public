using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Lifecycle
{
    internal static class PendingOperationLog
    {
        public static readonly LogContext Log = new("PendingOperation");
    }

    /// <summary>
    /// Marker component for entities representing in-flight atomic operations across
    /// save/load boundaries. See <c>Docs/Architecture/Core/SAVE_LOAD_LIFECYCLE_DOCTRINE.md</c>.
    ///
    /// Discovery use: PostLoadValidationSystem (or any audit/diagnostic system) can query
    /// all entities tagged with this component to enumerate pending operations after load.
    /// Domain consumers don't depend on the tag — they query by payload type.
    /// </summary>
    public struct PendingOperationTag : IComponentData, IEmptySerializable
    {
        // IEmptySerializable: no payload, but the component MUST round-trip so the
        // pending-operation entity keeps its discovery tag across save/load. A
        // DamageChargeRequest (or any payload) whose tag/phase did not persist is
        // orphaned forever — never matched by the consumer's pending query and the
        // durable side-effect (e.g. the counterfeit-fire repair charge) is lost.
        public void SetDefaults() => this = default;
    }

    /// <summary>
    /// Phase progression for a <see cref="PendingOperationTag"/> entity.
    ///
    /// Queued    — entity created, payload set, side effects NOT applied yet.
    /// Applied   — side effects applied to durable state (singleton/components).
    ///             Acts as the idempotency guard: on save/load, a consumer must skip
    ///             apply when phase is already Applied.
    /// Confirmed — terminal phase. The entity will be destroyed this frame.
    ///             Mostly informational; entities are typically destroyed via ECB
    ///             in the same step that sets Confirmed.
    /// </summary>
    public enum PendingPhaseValue : byte
    {
        Queued = 0,
        Applied = 1,
        Confirmed = 2,
    }

    /// <summary>
    /// State machine for an atomic operation. See <see cref="PendingOperationTag"/>.
    /// </summary>
    public struct PendingPhase : IComponentData, ISerializable
    {
        public PendingPhaseValue Value;

        public bool IsQueued => Value == PendingPhaseValue.Queued;
        public bool IsApplied => Value == PendingPhaseValue.Applied;
        public bool IsConfirmed => Value == PendingPhaseValue.Confirmed;

        // The phase is the idempotency guard and MUST persist: a save while a
        // pending op is Applied (durable side-effect done, entity not yet
        // destroyed) must NOT re-apply on load. Without serialization the
        // component is dropped on load and the consumer's pending query no
        // longer matches the entity at all (W2 HIGH: lost durable side-effect).
        private const byte SAVE_VERSION = 1;

        public void SetDefaults() => this = default; // Value = Queued (0)

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "ph", (int)Value);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PendingPhase)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "ph":
                            {
                                int phv = KeyedSerializer.ReadBoundedInt(reader, tag, "ph", 0, 2, 0);
                                // Corrupt/out-of-range → Queued: the conservative
                                // default is "re-process the op", never silently
                                // skip a durable side-effect.
                                Value = System.Enum.IsDefined(typeof(PendingPhaseValue), (byte)phv)
                                    ? (PendingPhaseValue)(byte)phv
                                    : PendingPhaseValue.Queued;
                                break;
                            }
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PendingOperationLog.Log.Error($"Deserialize {nameof(PendingPhase)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Helpers for queueing and resolving pending operations.
    /// See <c>Docs/Architecture/Core/SAVE_LOAD_LIFECYCLE_DOCTRINE.md</c> for the doctrine.
    /// </summary>
    public static class PendingOperationExtensions
    {
        /// <summary>
        /// Queue a new pending operation via an EntityCommandBuffer.
        ///
        /// Creates one entity carrying: the payload, the <see cref="PendingOperationTag"/>,
        /// and an initial <see cref="PendingPhase"/> at <see cref="PendingPhaseValue.Queued"/>.
        /// The entity is the single durable source of truth for the operation — no parallel
        /// persisted flag should be written by the caller.
        /// </summary>
        public static Entity QueuePendingOperation<TPayload>(
            this EntityCommandBuffer ecb,
            in TPayload payload)
            where TPayload : unmanaged, IComponentData
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, payload);
            ecb.AddComponent<PendingOperationTag>(e);
            ecb.AddComponent(e, new PendingPhase { Value = PendingPhaseValue.Queued });
            return e;
        }
    }
}
