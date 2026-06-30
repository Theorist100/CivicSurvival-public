using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Domain.Economy
{
    /// <summary>
    /// Pause-safe civilian repair transaction intent.
    ///
    /// Pipeline: CivilianRepairDetectorSystem creates intent → CivilianRepairPaymentSystem
    /// resolves budget synchronously in ModificationEnd → CivilianRepairCommitSystem asks
    /// CivilianDamageSystem to apply/refund and destroys the intent.
    ///
    /// Pre-release: SAVE_VERSION starts at 1. No legacy save migration —
    /// SaveVersions.GLOBAL = 1 and no old saves exist in the wild, so the
    /// previously bumped v2/v3 was internal development churn that we restart.
    /// </summary>
    public struct CivilianRepairIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        /// <summary>Vanilla building reference (typed Index+Version).</summary>
        [TxTarget]
        public BuildingRef Building;

        /// <summary>Stable per-repair correlation id assigned by the detector.</summary>
        [TxId]
        public int RepairId;

        /// <summary>Effective cost to deduct. ShadowOps stores post-markup wallet cost.</summary>
        public int Cost;

        /// <summary>Exact kickback amount for ShadowIncomeRequest on success (0 if no kickback).</summary>
        public int KickbackAmount;

        /// <summary>RepairType enum as byte — needed for logging on success.</summary>
        public byte RepairTypeByte;

        /// <summary>Repair duration in game hours — needed for setting RepairEndHour on success.</summary>
        public float DurationHours;

        /// <summary>True after payment system has resolved the synchronous budget attempt.</summary>
        public bool BudgetResolved;

        /// <summary>True when the synchronous budget deduction succeeded.</summary>
        public bool BudgetSucceeded;

        /// <summary>Bridge request id carried from the UI command.</summary>
        public int RequestId;

        /// <summary>
        /// Runtime-only idempotence guard. Intentionally not serialized: a save can
        /// retain the intent while queued ECB apply/destroy commands are lost, so load
        /// must reprocess from the persisted BudgetResolved/BudgetSucceeded state.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public RepairType RepairType => (RepairType)RepairTypeByte;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;
        private const float MAX_DURATION_HOURS = 10000f;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 9);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "repId", RepairId);
                KeyedSerializer.WriteField(writer, "cost", Cost);
                KeyedSerializer.WriteField(writer, "kick", KickbackAmount);
                KeyedSerializer.WriteField(writer, "repType", RepairTypeByte);
                KeyedSerializer.WriteField(writer, "dur", DurationHours);
                KeyedSerializer.WriteField(writer, "bRes", BudgetResolved);
                KeyedSerializer.WriteField(writer, "bOk", BudgetSucceeded);
                KeyedSerializer.WriteField(writer, "rqId", RequestId);
                // Applied is intentionally NOT serialized — runtime-only guard.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CivilianRepairIntent)))
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
                            case "repId": RepairId = KeyedSerializer.ReadBoundedInt(reader, tag, "repId", 0, int.MaxValue, 0); break;
                            case "cost": Cost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "kick": KickbackAmount = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "repType": RepairTypeByte = KeyedSerializer.ReadBoundedByte(reader, tag, "repType", 0, 2, 0); break;
                            case "dur": DurationHours = KeyedSerializer.ReadSafeFloat(reader, tag, "dur", 0f, MAX_DURATION_HOURS, 0f); break;
                            case "bRes": BudgetResolved = KeyedSerializer.ReadBool(reader, tag, "bRes"); break;
                            case "bOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "bOk"); break;
                            case "rqId": RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "rqId", 0, int.MaxValue, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(CivilianRepairIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
