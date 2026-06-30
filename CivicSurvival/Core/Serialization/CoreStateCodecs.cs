using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Pure codecs for small core save-state payloads.
    /// They deliberately contain no World, EntityManager, Mod, or service state.
    /// </summary>
    public static class DayChangedDedupCodec
    {
        public const int MaxLastProcessedDay = 100000;

        public static void Write<TWriter>(int lastProcessedDay, TWriter writer)
            where TWriter : IWriter
            => writer.Write(lastProcessedDay);

        public static int Read<TReader>(TReader reader)
            where TReader : IReader
            => SerializationGuard.ReadMonotonicCounter(reader, 0, MaxLastProcessedDay, "LastProcessedDay");

        public static int NormalizeFromSave(int lastProcessedDay)
            => System.Math.Clamp(lastProcessedDay, 0, MaxLastProcessedDay);
    }

    public readonly struct SaveMetadataState
    {
        public SaveMetadataState(byte formatVersion, string modVersion)
        {
            FormatVersion = formatVersion;
            ModVersion = modVersion ?? string.Empty;
        }

        public byte FormatVersion { get; }
        public string ModVersion { get; }
    }

    public static class SaveMetadataCodec
    {
        public static void Write<TWriter>(in SaveMetadataState state, TWriter writer)
            where TWriter : IWriter
        {
            writer.Write(state.FormatVersion);
            writer.Write(state.ModVersion);
        }

        public static void Read<TReader>(TReader reader, out SaveMetadataState state)
            where TReader : IReader
        {
            reader.Read(out byte formatVersion);
            reader.Read(out string modVersion);
            state = new SaveMetadataState(formatVersion, modVersion);
        }
    }
}
