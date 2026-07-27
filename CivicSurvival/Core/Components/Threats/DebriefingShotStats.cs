using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class DebriefingShotStatsLog
    {
        public static readonly LogContext Log = new("DebriefingShotStats");
    }

    /// <summary>
    /// Debriefing: AA shots fired this wave.
    /// Single Writer: AirDefenseShotStatsFlushSystem (drains AA + ballistic shot counters).
    /// Split from WaveDebriefingData to prevent ECB full-struct stomp.
    /// </summary>
    public struct DebriefingShotStats : IComponentData, ISerializable
    {
        /// <summary>Total AA shots fired this wave.</summary>
        public int ShotsFired;

        /// <summary>Gun rounds (Heritage / Bofors / Gepard) fired this wave — the non-Patriot slice of
        /// <see cref="ShotsFired"/>. For developer balance telemetry (balance.wave_result).</summary>
        public int RoundsConsumed;

        /// <summary>Patriot interceptor missiles fired this wave — the Patriot slice of
        /// <see cref="ShotsFired"/>. For developer balance telemetry (balance.wave_result).</summary>
        public int MissilesConsumed;

        public void Reset() => SetDefaults();

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
                DebriefingStatsCodec.WriteShot(this, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DebriefingShotStats)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    DebriefingStatsCodec.ReadShot(reader, this, out var stats);
                    this = stats;
                }
            }
            catch (System.Exception ex)
            {
                DebriefingShotStatsLog.Log.Error($"Deserialize {nameof(DebriefingShotStats)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


