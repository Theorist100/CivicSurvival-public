using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces;

namespace CivicSurvival.Core.Components.Requests
{
    internal static class GrantDonorPatriotCreditsRequestLog
    {
        public static readonly LogContext Log = new("GrantDonorPatriotCreditsRequest");
    }

    internal static class ForceCrewReleaseRequestLog
    {
        public static readonly LogContext Log = new("ForceCrewReleaseRequest");
    }

    /// <summary>
    /// Request to grant donor Patriot credits (from donor conference Defense aid).
    /// Processed by AirDefenseStateSystem (single writer).
    /// Implements ISerializable to survive autosave between ECB playback (frame N end)
    /// and processing (frame N+1) — prevents silent credit loss on save/load.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct GrantDonorPatriotCreditsRequest : IComponentData, ICommandRequest, ISerializable
    {
        public int Credits;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "cr", Credits);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(GrantDonorPatriotCreditsRequest)))
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
                            case "cr": Credits = KeyedSerializer.ReadBoundedInt(reader, tag, "cr", 0, 1000000, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                GrantDonorPatriotCreditsRequestLog.Log.Error($"Deserialize {nameof(GrantDonorPatriotCreditsRequest)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Request to set ammo on AA installation (resupply).
    /// Created by: AAAmmoSystem (wave end, emergency resupply)
    /// Processed by: AARequestProcessorSystem (single writer)
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ResupplyAARequest : IComponentData, ICommandRequest, ISerializable
    {
        [TxTarget]
        public int AAEntityIndex;
        [TxTarget]
        public int AAEntityVersion;
        public int NewAmmo;
        /// <summary>S16b-8 FIX: Rounds added (for refund if AA destroyed before processing).</summary>
        public int RoundsAdded;
        /// <summary>R4-S9-05 FIX: Original cost per round at purchase time (for accurate refund).</summary>
        public int CostPerRound;
        public long AllocatedCost;

        public long RefundCost => AllocatedCost > 0 ? AllocatedCost : (long)RoundsAdded * CostPerRound;

        /// <summary>
        /// Set true the moment AARequestProcessorSystem decides this request's
        /// outcome (ammo set OR dead-AA refund), before the deferred ECB destroy
        /// plays back. The ammo set is value-idempotent (absolute clamp), but the
        /// dead-AA refund emits BudgetEmitter.QueueAddFunds — at 2x-3x the request
        /// is still alive on later ticks of the same frame, so without this guard
        /// the refund double-emits (real budget bug). Skip-if-Applied makes
        /// processing exactly-once per request.
        ///
        /// RUNTIME-ONLY — intentionally NOT serialized (WriteBlockHeader is 4,
        /// the persisted fields aa/ammo/added/cpr; Applied is not among them).
        /// A request that survives a save taken before its destroy/refund ECB
        /// played back must be reprocessed after load (Applied default-false by
        /// design); the persisted RoundsAdded/CostPerRound drive the post-load
        /// refund exactly once. Mirrors AAPlacementIntent.Applied.
        /// </summary>
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                // Entity tag rides the engine m_EntityTable remap — raw idx/ver
                // ints do NOT survive load (recycled slot → wrong/empty AA).
                KeyedSerializer.WriteEntityField(writer, "aa", new Entity { Index = AAEntityIndex, Version = AAEntityVersion });
                KeyedSerializer.WriteField(writer, "ammo", NewAmmo);
                KeyedSerializer.WriteField(writer, "added", RoundsAdded);
                KeyedSerializer.WriteField(writer, "cpr", CostPerRound);
                KeyedSerializer.WriteField(writer, "alc", AllocatedCost);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
                // A save-surviving request must be reprocessed after load. See field doc.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ResupplyAARequest)))
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
                            case "aa": { var e = KeyedSerializer.ReadEntity(reader, tag, "aa"); AAEntityIndex = e.Index; AAEntityVersion = e.Version; break; }
                            case "ammo": NewAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "ammo", 0, 100000, 0); break;
                            case "added": RoundsAdded = KeyedSerializer.ReadBoundedInt(reader, tag, "added", 0, 100000, 0); break;
                            case "cpr": CostPerRound = KeyedSerializer.ReadBoundedInt(reader, tag, "cpr", 0, 1000000, 0); break;
                            case "alc": AllocatedCost = KeyedSerializer.ReadBoundedLong(reader, tag, "alc", 0, long.MaxValue); break;
                            // 'appl' intentionally not read — Applied is runtime-only and
                            // must be default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Request to force-release crew from AA installation due to manpower deficit.
    /// Created by: MobilizationSystem.ForceReleaseExcess() when population exodus
    /// shrinks manpower pool below used amount.
    /// Processed by: AARequestProcessorSystem (single writer) — zeros CrewAssigned.
    /// Implements ISerializable to survive autosave between ECB playback and processing
    /// (MOB-03: prevents manpower desync on save/load).
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ForceCrewReleaseRequest : IComponentData, ICommandRequest, ISerializable
    {
        public int AAEntityIndex;
        public int AAEntityVersion;
        public int NewCrewCount;
        [TxRuntimeGuard]
        public bool Applied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteEntityField(writer, "aa", new Entity { Index = AAEntityIndex, Version = AAEntityVersion });
                KeyedSerializer.WriteField(writer, "crew", NewCrewCount);
                // 'Applied' is intentionally NOT serialized — runtime-only guard.
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ForceCrewReleaseRequest)))
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
                            case "aa": { var e = KeyedSerializer.ReadEntity(reader, tag, "aa"); AAEntityIndex = e.Index; AAEntityVersion = e.Version; break; }
                            case "crew": NewCrewCount = KeyedSerializer.ReadBoundedInt(reader, tag, "crew", 0, 1000, 0); break;
                            // 'appl' intentionally not read — Applied is runtime-only and
                            // must be default false after load.
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ForceCrewReleaseRequestLog.Log.Error($"Deserialize {nameof(ForceCrewReleaseRequest)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
