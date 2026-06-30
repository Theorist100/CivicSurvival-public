using Unity.Entities;
using Unity.Collections;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// Typed link from a retained BudgetDeductRequest entity to an AA resupply batch.
    /// This is the authoritative budget-result identity; Source remains diagnostic only.
    /// </summary>
    public struct AAResupplyBudgetLink : IComponentData, ISerializable
    {
        public long BatchId;

        /// <summary>
        /// Runtime-only terminal marker for retained budget-result requests whose
        /// batch already disappeared. The request destroy is deferred via ECB, so
        /// 2x/3x simulation ticks can see the same result again before playback.
        /// This flag makes the refund/destroy decision retire-once without
        /// persisting a volatile terminal state across save/load.
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
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AAResupplyBudgetLink)))
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
                Mod.Log.Error($"Deserialize {nameof(AAResupplyBudgetLink)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Batch-level paid/free AA resupply intent.
    /// One batch represents one resupply decision over a frozen set of AA line items.
    /// </summary>
    public struct AAResupplyBatchIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        /// <summary>
        /// Stable per-batch correlation id assigned at batch creation (monotonic,
        /// always non-zero). Budget result is matched to THIS batch by BatchId — not
        /// by AA identity (ambiguous across save/load, re-resupply, or two pending
        /// batches over the same AA set).
        /// </summary>
        [TxId]
        public long BatchId;
        public long TotalCost;
        public bool RequiresBudget;
        public bool BudgetResolved;
        public bool BudgetSucceeded;
        public bool IsFullResupply;
        public int RequestedRounds;
        public int NeededRounds;
        public bool IsEmergency;
        public int RequestId;

        /// <summary>
        /// True for graduated trickle refill lines created per-tick during the calm
        /// phase. Trickle batches are silent — <see cref="AAResupplyPipelineSystem"/>
        /// does NOT publish an <c>AAResupplyEvent</c> on their apply (a per-tick
        /// Partial/Full would spam the narrative + telemetry listeners). The single
        /// terminal Full / Failed for the auto refill cycle is published by the owner
        /// (<c>AAAmmoSystem</c>) when the city-wide deficit actually reaches zero.
        /// </summary>
        public bool Trickle;

        /// <summary>
        /// Set true the moment Phase 2 decides this batch's outcome (apply OR
        /// drop), before the deferred ECB destroy plays back. CS2 barriers play
        /// back after ALL sim ticks, so at 2x-3x the batch is still alive on later
        /// ticks of the same frame — this guard makes Phase 2 idempotent per-batch
        /// (skip already-decided), so it cannot re-charge ammo+budget or re-emit
        /// the line ResupplyAARequests for the very batch it just applied. Data
        /// write (not structural) → visible on the next tick immediately, unlike a
        /// deferred tag component. The frozen lines are covered transitively: once
        /// the batch is Applied it is skipped, so ProcessSuccessfulBatchLines never
        /// runs twice for the same BatchId.
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized (do not add it to Serialize).
        /// It records that the apply/destroy ECB commands
        /// were *queued*, but those commands live in the ECB until barrier playback
        /// and do NOT survive a save taken before playback. Persisting Applied=true
        /// would make a surviving batch be skipped forever after load while its
        /// ammo+budget were never applied — the exact save-safe invariant this
        /// pipeline guarantees. After load Applied is default false by design, so
        /// the surviving batch is reprocessed and the persisted BudgetResolved /
        /// BudgetSucceeded (whose effect IS persistent) drive the post-load
        /// outcome — never the volatile Applied. Mirrors AAPlacementIntent.Applied.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 3;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 11);
                KeyedSerializer.WriteField(writer, "bid", BatchId);
                KeyedSerializer.WriteField(writer, "cost", TotalCost);
                KeyedSerializer.WriteField(writer, "reqB", RequiresBudget);
                KeyedSerializer.WriteField(writer, "bRes", BudgetResolved);
                KeyedSerializer.WriteField(writer, "bOk", BudgetSucceeded);
                KeyedSerializer.WriteField(writer, "full", IsFullResupply);
                KeyedSerializer.WriteField(writer, "rds", RequestedRounds);
                KeyedSerializer.WriteField(writer, "need", NeededRounds);
                KeyedSerializer.WriteField(writer, "emer", IsEmergency);
                KeyedSerializer.WriteField(writer, "rqId", RequestId);
                KeyedSerializer.WriteField(writer, "trk", Trickle);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
                // Persisting it would skip a save-surviving batch forever (its
                // apply/destroy ECB never played back). See field doc.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AAResupplyBatchIntent)))
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
                            case "cost": TotalCost = KeyedSerializer.ReadBoundedLong(reader, tag, "cost", 0, long.MaxValue); break;
                            case "reqB": RequiresBudget = KeyedSerializer.ReadBool(reader, tag, "reqB"); break;
                            case "bRes": BudgetResolved = KeyedSerializer.ReadBool(reader, tag, "bRes"); break;
                            case "bOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "bOk"); break;
                            case "full": IsFullResupply = KeyedSerializer.ReadBool(reader, tag, "full"); break;
                            case "rds": RequestedRounds = KeyedSerializer.ReadBoundedInt(reader, tag, "rds", 0, 100000, 0); break;
                            case "need": NeededRounds = KeyedSerializer.ReadBoundedInt(reader, tag, "need", 0, 100000, 0); break;
                            case "emer": IsEmergency = KeyedSerializer.ReadBool(reader, tag, "emer"); break;
                            case "rqId": RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "rqId", 0, int.MaxValue, 0); break;
                            case "trk": Trickle = KeyedSerializer.ReadBool(reader, tag, "trk"); break;
                            // 'appl' intentionally not read — Applied is runtime-only and
                            // must be default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(AAResupplyBatchIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Frozen AA line item belonging to an AAResupplyBatchIntent.
    /// New AA created after the batch is captured never receives ammo from that batch.
    ///
    /// ITransactionLifecycle: the line carries no own terminal guard — apply is
    /// gated transitively by its batch's <see cref="AAResupplyBatchIntent.Applied"/>
    /// / BudgetResolved. Once the batch is Applied (and skipped on later same-frame
    /// ticks) ProcessSuccessfulBatchLines never re-runs for this BatchId, so the
    /// line cannot be double-applied. BatchId is the stable correlation id;
    /// AAEntityIndex/Version is the Axiom 11 target identity.
    /// </summary>
    public struct AAResupplyLineIntent : IComponentData, ISerializable, ITransactionLifecycle
    {
        [TxId]
        public long BatchId;
        [TxTarget]
        public int AAEntityIndex;
        [TxTarget]
        public int AAEntityVersion;
        public int NewAmmo;
        public int RoundsAdded;
        public int CostPerRound;
        public long AllocatedCost;

        public long Cost => AllocatedCost > 0 ? AllocatedCost : (long)RoundsAdded * CostPerRound;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 6);
                KeyedSerializer.WriteField(writer, "bid", BatchId);
                // Entity tag rides the engine m_EntityTable remap — raw idx/ver
                // ints do NOT survive load (recycled slot → wrong/empty AA).
                KeyedSerializer.WriteEntityField(writer, "aa", new Entity { Index = AAEntityIndex, Version = AAEntityVersion });
                KeyedSerializer.WriteField(writer, "amm", NewAmmo);
                KeyedSerializer.WriteField(writer, "rds", RoundsAdded);
                KeyedSerializer.WriteField(writer, "cpr", CostPerRound);
                KeyedSerializer.WriteField(writer, "alc", AllocatedCost);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AAResupplyLineIntent)))
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
                            case "aa": { var e = KeyedSerializer.ReadEntity(reader, tag, "aa"); AAEntityIndex = e.Index; AAEntityVersion = e.Version; break; }
                            case "amm": NewAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "amm", 0, 100000, 0); break;
                            case "rds": RoundsAdded = KeyedSerializer.ReadBoundedInt(reader, tag, "rds", 0, 100000, 0); break;
                            case "cpr": CostPerRound = KeyedSerializer.ReadBoundedInt(reader, tag, "cpr", 0, 1000000, 0); break;
                            case "alc": AllocatedCost = KeyedSerializer.ReadBoundedLong(reader, tag, "alc", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(AAResupplyLineIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    /// <summary>
    /// Durable owner marker for retained AA resupply refund transports.
    /// </summary>
    public struct AAResupplyRefundIntent : IComponentData, ISerializable
    {
        public long Amount;
        public FixedString128Bytes OperationKey;

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
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out _, out var block, nameof(AAResupplyRefundIntent)))
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
                            OperationKey = new FixedString128Bytes(KeyedSerializer.ReadString(reader, tag, "op") ?? string.Empty);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, tag);
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(AAResupplyRefundIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
