using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Engineering
{
    /// <summary>
    /// Pause-safe plant-repair transaction intent created and consumed in ModificationEnd.
    ///
    /// Serializable: survives save/load so payment and repair application cannot be
    /// split by a save. After load, unresolved intents are paid again; resolved paid
    /// intents are committed without re-deducting.
    ///
    /// Two-phase repair transaction:
    /// Phase 1: PlantRepairPaymentSystem writes BudgetResolved/BudgetSucceeded.
    /// Phase 2: PlantRepairCommitSystem applies or rejects the resolved intent.
    /// </summary>
    public struct RepairTransactionIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        [TxId]
        public int IntentId;

        [TxTarget]
        public int PlantId;

        [TxTarget]
        public BuildingRef Building;

        public byte RepairTypeByte;
        public int Cost;
        public int KickbackAmount;
        public float DurationHours;
        public bool BudgetResolved;
        public bool BudgetSucceeded;
        public int RequestId;

        public RepairType RepairType => (RepairType)RepairTypeByte;

        /// <summary>
        /// Set true the moment Phase 2 decides this intent's outcome (apply OR reject),
        /// before the deferred ECB destroy plays back. CS2 barriers play back after ALL
        /// sim ticks, so at 2x-3x the intent is still alive on later ticks of the same
        /// frame; this guard makes Phase 2 idempotent per-intent.
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized. It records that apply/destroy
        /// ECB commands were queued, but those commands do NOT survive a save taken
        /// before playback. After load Applied is default false by design, so the
        /// surviving intent is reprocessed using persisted BudgetResolved/BudgetSucceeded.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;
        public const float MinimumDurationHours = 0.1f;
        public const float MunicipalDurationFallbackHours = 24f;
        public const float ShadowOpsDurationFallbackHours = 2f;
        public const float MaxDurationHours = 10000f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 10);
                KeyedSerializer.WriteField(writer, "id", IntentId);
                KeyedSerializer.WriteField(writer, "pid", PlantId);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "repType", (int)RepairTypeByte);
                KeyedSerializer.WriteField(writer, "cost", Cost);
                KeyedSerializer.WriteField(writer, "kick", KickbackAmount);
                KeyedSerializer.WriteField(writer, "dur", DurationHours);
                KeyedSerializer.WriteField(writer, "bRes", BudgetResolved);
                KeyedSerializer.WriteField(writer, "bOk", BudgetSucceeded);
                KeyedSerializer.WriteField(writer, "rqId", RequestId);
                // Applied is intentionally not serialized; see field doc.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(RepairTransactionIntent)))
            { SetDefaults(); return; }
            try
            {
                DurationHours = -1f;
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "id": IntentId = KeyedSerializer.ReadBoundedInt(reader, tag, "id", 0, int.MaxValue, 0); break;
                            case "pid": PlantId = KeyedSerializer.ReadBoundedInt(reader, tag, "pid", 0, int.MaxValue, 0); break;
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "repType": RepairTypeByte = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "repType", 0, 2, 0); break;
                            case "cost": Cost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "kick": KickbackAmount = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "dur": DurationHours = KeyedSerializer.ReadSafeFloat(reader, tag, "dur", MinimumDurationHours, MaxDurationHours, -1f); break;
                            case "bRes": BudgetResolved = KeyedSerializer.ReadBool(reader, tag, "bRes"); break;
                            case "bOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "bOk"); break;
                            case "rqId": RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "rqId", 0, int.MaxValue, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }

                if (!GameDurationHours.TryCreate(DurationHours, out _, MaxDurationHours))
                    DurationHours = RepairType == RepairType.ShadowOps
                        ? ShadowOpsDurationFallbackHours
                        : MunicipalDurationFallbackHours;
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(RepairTransactionIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
