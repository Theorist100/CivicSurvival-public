using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Requests
{
    internal static class WarDamageBudgetIntentLog
    {
        public static readonly LogContext Log = new("WarDamageBudgetIntent");
    }

    /// <summary>
    /// Correlates a retained wave-damage budget/debt request back to the durable
    /// wave charge owner in WarDamageDebtSystem.
    /// </summary>
    public struct WarDamageBudgetIntent : IComponentData, ISerializable
    {
        public int WaveNumber;
        public long Amount;
        public bool ReissueQueued;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "wave", WaveNumber);
                KeyedSerializer.WriteField(writer, "amt", Amount);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(WarDamageBudgetIntent)))
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
                            case "wave": WaveNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "wave", -1, int.MaxValue, -1); break;
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                WarDamageBudgetIntentLog.Log.Error($"Deserialize {nameof(WarDamageBudgetIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
