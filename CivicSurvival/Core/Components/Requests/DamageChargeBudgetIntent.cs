using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Requests
{
    internal static class DamageChargeBudgetIntentLog
    {
        public static readonly LogContext Log = new("DamageChargeBudgetIntent");
    }

    /// <summary>
    /// Correlation payload attached to a <see cref="BudgetResultMode.RetainResult"/>
    /// <c>BudgetDeductRequest</c> emitted by DamageAccountingSystem for a fire /
    /// explosion repair charge. Lets the drain map a resolved
    /// <c>BudgetDeductResult</c> back to its origin so the durable marker is
    /// settled ONLY once the deduction is actually confirmed
    /// (SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 5).
    ///
    /// Why RetainResult (not FireAndForget): a FireAndForget request that is
    /// created but not yet processed when a save lands is destroyed on load by
    /// BudgetResolutionSystem.ValidateAfterLoad → the charge is silently lost
    /// while the marker said "settled". RetainResult instead expires unprocessed
    /// requests to <c>BudgetDeductResult{Succeeded=false}</c>, which the drain
    /// re-issues from — the marker never settles until a real success.
    ///
    /// <see cref="ISerializable"/> so it round-trips with its RetainResult budget
    /// entity; the building ref is written via <c>WriteEntityField</c> so vanilla
    /// remaps it on load.
    /// </summary>
    public struct DamageChargeBudgetIntent : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>
        /// Origin classification: Explosion → settle EquipmentWear.ExplosionChargeSettled;
        /// Fire / generic → confirm/destroy the originating PendingOperation&lt;DamageChargeRequest&gt;.
        /// </summary>
        public DamageType Type;

        /// <summary>
        /// F-14 (ACC-05): the exact originating PendingOperation&lt;DamageChargeRequest&gt;
        /// entity for the Fire / generic durable path. Written via WriteEntityField
        /// so vanilla remaps it on load; the drain resolves THIS op instead of a
        /// (building,type) FIFO scan, so two concurrent charges on the same
        /// building can never confirm/destroy the wrong op. Entity.Null for the
        /// Explosion path (it settles EquipmentWear by building instead).
        /// </summary>
        public EntityRef OpRef;

        public Entity GetBuildingEntity() => Building.ToEntity();

        public void SetDefaults() => this = default;

        // v2: added OpRef. Keyed format tolerates a
        // missing "op" key, so a v1 block simply leaves OpRef = Entity.Null.
        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "type", (int)Type);
                KeyedSerializer.WriteEntityField(writer, "op", OpRef.ToEntity());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DamageChargeBudgetIntent)))
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
                            case "op": OpRef = EntityRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "op")); break;
                            case "type":
                            {
                                int tv = KeyedSerializer.ReadBoundedInt(reader, tag, "type", 0, 255, 0);
                                // Corrupt/unknown → Operational (0): a wrong route
                                // (no matching fire PendingOp / explosion marker)
                                // simply drops this one entity, never double-charges.
                                Type = System.Enum.IsDefined(typeof(DamageType), (byte)tv)
                                    ? (DamageType)(byte)tv
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
                DamageChargeBudgetIntentLog.Log.Error($"Deserialize {nameof(DamageChargeBudgetIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
