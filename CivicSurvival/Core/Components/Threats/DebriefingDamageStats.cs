using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class DebriefingDamageStatsLog
    {
        public static readonly LogContext Log = new("DebriefingDamageStats");
    }

    /// <summary>
    /// Debriefing: damage statistics this wave (casualties, buildings).
    /// Single Writer: ThreatDamageSystem (via DebriefingHelper batch flush).
    /// Split from WaveDebriefingData to prevent ECB full-struct stomp.
    /// </summary>
    public struct DebriefingDamageStats : IComponentData, ISerializable
    {
        /// <summary>Total civilian casualties this wave.</summary>
        public int Casualties;

        /// <summary>Estimated damage cost in $ this wave.</summary>
        public int DamageCost;

        /// <summary>Buildings destroyed this wave.</summary>
        public int BuildingsDestroyed;

        /// <summary>Buildings set on fire this wave.</summary>
        public int BuildingsOnFire;

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
                DebriefingStatsCodec.WriteDamage(this, writer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DebriefingDamageStats)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    DebriefingStatsCodec.ReadDamage(reader, this, out var stats);
                    this = stats;
                }
            }
            catch (System.Exception ex)
            {
                DebriefingDamageStatsLog.Log.Error($"Deserialize {nameof(DebriefingDamageStats)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}


