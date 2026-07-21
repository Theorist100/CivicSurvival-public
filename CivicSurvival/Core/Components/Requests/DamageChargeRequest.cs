using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Requests
{
    internal static class DamageChargeRequestLog
    {
        public static readonly LogContext Log = new("DamageChargeRequest");
    }

    /// <summary>
    /// Durable outbox payload for an immediate damage repair charge that must
    /// survive save/load (SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 5,
    /// <c>OwnsDurableOutbox</c>). Queued via
    /// <c>ecb.QueuePendingOperation(new DamageChargeRequest { ... })</c> by a
    /// producer that has no pre-existing durable damage marker to reconcile from
    /// (CounterfeitBatteryFireSystem). Consumed by DamageAccountingSystem, which
    /// issues the BudgetDeductRequest and destroys the entity.
    ///
    /// Unlike the transient <c>DamageAppliedEvent</c> (one-frame,
    /// IEmptySerializable), this is full <c>ISerializable</c>: the charge is the
    /// authoritative state and MUST roundtrip. The building ref is written via
    /// <c>WriteEntityField</c> so vanilla remaps it on load.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct DamageChargeRequest : IComponentData, ICommandRequest, ISerializable, IBuildingLinked
    {
        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Repair cost in $ to deduct (with infrastructure debt fallback).</summary>
        public long EstimatedRepairCost;

        /// <summary>Damage source classification (for logging/telemetry).</summary>
        public DamageType Type;

        public Entity GetBuildingEntity() => Building.ToEntity();

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "cost", EstimatedRepairCost);
                KeyedSerializer.WriteField(writer, "type", (int)Type);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DamageChargeRequest)))
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
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "cost": EstimatedRepairCost = KeyedSerializer.ReadBoundedLong(reader, tag, "cost", 0, long.MaxValue); break;
                            case "type":
                            {
                                int rawType = KeyedSerializer.ReadBoundedInt(reader, tag, "type", 0, 255, 0);
                                Type = System.Enum.IsDefined(typeof(DamageType), (byte)rawType)
                                    ? (DamageType)(byte)rawType
                                    : DamageType.Operational;
                                break;
                            }
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                DamageChargeRequestLog.Log.Error($"Deserialize {nameof(DamageChargeRequest)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
