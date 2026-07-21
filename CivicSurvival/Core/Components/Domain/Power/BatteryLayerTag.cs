using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Power
{
    internal static class BatteryLayerTagLog
    {
        public static readonly LogContext Log = new("BatteryLayerTag");
    }

    /// <summary>
    /// Battery layer classification for three-layer battery system.
    /// Private = R/C/I/O, Hospital = critical medical, School = social.
    /// </summary>
    public enum BatteryLayer : byte
    {
        Private = 0,
        Hospital = 1,
        School = 2
    }

    /// <summary>
    /// Tag component on BackupPower mod entities to classify battery layer.
    /// Used for per-layer stats aggregation and coverage-based mitigation.
    /// </summary>
    public struct BatteryLayerTag : IComponentData, ISerializable
    {
        public BatteryLayer Layer;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteEnumByteField(writer, "lyr", (byte)Layer);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BatteryLayerTag)))
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
                            case "lyr": Layer = KeyedSerializer.ReadEnumByte<TReader, BatteryLayer>(reader, tag, "lyr", BatteryLayer.Private); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                BatteryLayerTagLog.Log.Error($"Deserialize {nameof(BatteryLayerTag)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}


