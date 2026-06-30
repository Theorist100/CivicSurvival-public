using CivicSurvival.Core.Components.Domain.GridWarfare;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Pure value codec for the <see cref="CounterAttackArsenal"/> save block.
    /// No World / EntityManager / RNG — that glue stays in the owner system
    /// (<c>CounterAttackArsenalSystem</c>). Keyed format: unknown / missing keys
    /// default safely, so adding a munition kind later is non-breaking.
    /// </summary>
    public static class CounterAttackArsenalCodec
    {
        // Hard ceiling against corrupt/garbage saves; real balance ceiling is applied
        // by the owner system on Replenish.
        private const int MaxStockSanity = 1_000_000;

        // Sanity ceiling for the persisted procurement batch counter. A monotonic id
        // bumped once per purchase/grant never realistically approaches this; the bound
        // only guards against a corrupt save reseeding the counter to garbage.
        private const long MaxBatchIdSanity = 1_000_000_000L;

        public static void Write<TWriter>(in CounterAttackArsenal s, long nextBatchId, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "drone", s.DroneStock);
            KeyedSerializer.WriteField(writer, "ball", s.BallisticStock);
            KeyedSerializer.WriteField(writer, "nbid", nextBatchId);
        }

        public static void Read<TReader>(TReader reader, out CounterAttackArsenal state, out long nextBatchId)
            where TReader : IReader
        {
            int drone = 0;
            int ballistic = 0;
            // 0 = field absent (old save); the owner system treats it as "no persisted
            // counter" and keeps its live-batch-scan reseed.
            long next = 0;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "drone": drone = KeyedSerializer.ReadBoundedInt(reader, tag, "drone", 0, MaxStockSanity, 0); break;
                    case "ball": ballistic = KeyedSerializer.ReadBoundedInt(reader, tag, "ball", 0, MaxStockSanity, 0); break;
                    case "nbid": next = KeyedSerializer.ReadBoundedLong(reader, tag, "nbid", 0, MaxBatchIdSanity); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new CounterAttackArsenal
            {
                DroneStock = drone,
                BallisticStock = ballistic
            };
            nextBatchId = next;
        }
    }
}
