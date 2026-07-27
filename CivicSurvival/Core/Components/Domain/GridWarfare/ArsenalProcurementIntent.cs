using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Domain.GridWarfare
{
    /// <summary>
    /// Typed link from a retained <c>BudgetDeductRequest</c> entity to an arsenal
    /// procurement batch. This is the authoritative budget-result identity (the
    /// budget Source string remains diagnostic only). Mirror of
    /// <c>AAResupplyBudgetLink</c>.
    /// </summary>
    public struct ArsenalProcurementBudgetLink : IComponentData, ISerializable
    {
        public long BatchId;

        /// <summary>
        /// Runtime-only terminal marker for a retained budget-result request whose
        /// batch already disappeared. The request destroy is deferred via ECB, so
        /// 2x/3x simulation ticks can see the same result again before playback.
        /// Retire-once without persisting a volatile terminal state across save/load.
        /// </summary>
        public bool Retired;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "bid", BatchId);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ArsenalProcurementBudgetLink)))
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
                            case "bid": BatchId = KeyedSerializer.ReadBoundedLong(reader, tag, "bid", 1, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(ArsenalProcurementBudgetLink)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// One paid arsenal procurement decision: buy <see cref="Count"/> units of
    /// <see cref="Kind"/> for <see cref="TotalCost"/>, gated through the budget
    /// pipeline by <see cref="CounterAttackArsenalPipelineSystem"/>. Mirror of
    /// <c>AAResupplyBatchIntent</c>, minus the per-target line items — the arsenal is
    /// a single singleton stock, so the batch carries the granted amount itself.
    ///
    /// Channel (a) — paid import / donors. The shadow-import path routes its budget
    /// through <c>BudgetCategory.ShadowOps</c>, which applies SanctionsMarkup and the
    /// shadow-wallet pending reservation automatically inside <c>BudgetEmitter</c>.
    /// Donors enqueue the same intent with <see cref="RequiresBudget"/>=false (the aid
    /// is already paid for diplomatically).
    /// </summary>
    public struct ArsenalProcurementBatchIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        /// <summary>
        /// Stable per-batch correlation id assigned at creation (monotonic, non-zero).
        /// Budget result is matched to THIS batch by BatchId, not by arsenal identity.
        /// </summary>
        [TxId]
        public long BatchId;

        /// <summary>Munition kind being purchased.</summary>
        public ArsenalKind Kind;

        /// <summary>Number of units this batch grants on success (always &gt; 0).</summary>
        public int Count;

        /// <summary>Effective (post-markup) cost reserved for this batch.</summary>
        public long TotalCost;

        public bool RequiresBudget;
        public bool BudgetResolved;
        public bool BudgetSucceeded;

        /// <summary>
        /// Set true the moment the pipeline decides this batch's outcome (apply OR
        /// drop), before the deferred ECB destroy plays back. CS2 barriers play back
        /// after ALL sim ticks, so at 2x-3x the batch is still alive on later ticks of
        /// the same frame — this guard makes the apply idempotent per-batch (skip
        /// already-decided), so the same batch cannot re-grant stock or re-charge
        /// budget. Data write (not structural) → visible next tick immediately.
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized. The grant/destroy ECB commands
        /// live in the ECB until barrier playback and do NOT survive a save taken
        /// before playback. Persisting Applied=true would make a surviving batch be
        /// skipped forever after load while its stock was never granted. After load
        /// Applied is default false by design; the persisted BudgetResolved /
        /// BudgetSucceeded drive the post-load outcome. Mirrors
        /// <c>AAResupplyBatchIntent.Applied</c>.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteField(writer, "bid", BatchId);
                KeyedSerializer.WriteEnumIntField(writer, "kind", (int)Kind);
                KeyedSerializer.WriteField(writer, "cnt", Count);
                KeyedSerializer.WriteField(writer, "cost", TotalCost);
                KeyedSerializer.WriteField(writer, "reqB", RequiresBudget);
                KeyedSerializer.WriteField(writer, "bRes", BudgetResolved);
                KeyedSerializer.WriteField(writer, "bOk", BudgetSucceeded);
                // 'Applied' intentionally NOT serialized — runtime-only guard.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ArsenalProcurementBatchIntent)))
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
                            case "bid": BatchId = KeyedSerializer.ReadBoundedLong(reader, tag, "bid", 1, long.MaxValue); break;
                            case "kind": Kind = KeyedSerializer.ReadEnumInt<TReader, ArsenalKind>(reader, tag, "kind", ArsenalKind.Drone); break;
                            case "cnt": Count = KeyedSerializer.ReadBoundedInt(reader, tag, "cnt", 0, 100000, 0); break;
                            case "cost": TotalCost = KeyedSerializer.ReadBoundedLong(reader, tag, "cost", 0, long.MaxValue); break;
                            case "reqB": RequiresBudget = KeyedSerializer.ReadBool(reader, tag, "reqB"); break;
                            case "bRes": BudgetResolved = KeyedSerializer.ReadBool(reader, tag, "bRes"); break;
                            case "bOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "bOk"); break;
                            // 'appl' intentionally not read — Applied is runtime-only.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(ArsenalProcurementBatchIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Durable owner marker for a retained arsenal-procurement refund transport
    /// (a budget add-funds request issued when a charged batch could not be applied).
    /// Mirror of <c>AAResupplyRefundIntent</c>.
    /// </summary>
    public struct ArsenalProcurementRefundIntent : IComponentData, ISerializable
    {
        public long Amount;
        public Unity.Collections.FixedString128Bytes OperationKey;

        private const byte SAVE_VERSION = 1;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out _, out var block, nameof(ArsenalProcurementRefundIntent)))
            {
                SetDefaults();
                return;
            }

            try
            {
                int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int i = 0; i < fc; i++)
                {
                    var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "amt":
                            Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue);
                            break;
                        case "op":
                            OperationKey = new Unity.Collections.FixedString128Bytes(KeyedSerializer.ReadString(reader, tag, "op") ?? string.Empty);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, tag);
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(ArsenalProcurementRefundIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
