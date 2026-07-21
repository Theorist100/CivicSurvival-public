using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class DebriefingWaveStatsLog
    {
        public static readonly LogContext Log = new("DebriefingWaveStats");
    }

    /// <summary>
    /// Debriefing: wave-level statistics (lifecycle counters).
    /// Single Writer: WaveExecutor (reset on Alert, finalize on Recovery).
    /// Split from WaveDebriefingData to prevent ECB full-struct stomp.
    /// </summary>
    public struct DebriefingWaveStats : IComponentData, ISerializable
    {
        /// <summary>Wave number for this debriefing.</summary>
        public int WaveNumber;

        /// <summary>Total threats spawned this wave.</summary>
        public int TotalThreats;

        /// <summary>Total threats intercepted.</summary>
        public int Intercepted;

        /// <summary>Total threats that hit targets.</summary>
        public int Hits;

        /// <summary>Intercept rate = Intercepted / TotalThreats (0-100%).</summary>
        public readonly float InterceptRate =>
            TotalThreats > 0 ? (float)Intercepted / TotalThreats * 100f : 0f;

        public void Reset(int waveNumber)
        {
            WaveNumber = waveNumber;
            TotalThreats = 0;
            Intercepted = 0;
            Hits = 0;
        }

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteField(writer, "wave", WaveNumber);
                KeyedSerializer.WriteField(writer, "total", TotalThreats);
                KeyedSerializer.WriteField(writer, "intc", Intercepted);
                KeyedSerializer.WriteField(writer, "hits", Hits);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DebriefingWaveStats)))
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
                            case "wave": WaveNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "wave", 0, 10000, 0); break;
                            case "total": TotalThreats = KeyedSerializer.ReadBoundedInt(reader, tag, "total", 0, 10000, 0); break;
                            case "intc": Intercepted = KeyedSerializer.ReadBoundedInt(reader, tag, "intc", 0, 10000, 0); break;
                            case "hits": Hits = KeyedSerializer.ReadBoundedInt(reader, tag, "hits", 0, 10000, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                DebriefingWaveStatsLog.Log.Error($"Deserialize {nameof(DebriefingWaveStats)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


