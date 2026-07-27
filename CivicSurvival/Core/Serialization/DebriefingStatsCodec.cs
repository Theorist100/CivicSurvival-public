using CivicSurvival.Core.Components.Threats;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Pure value codecs for debriefing stat singleton payloads.
    /// Extracted from the component ISerializable bodies so the wire contract
    /// can be roundtrip-tested without Unity World/EntityManager state.
    /// </summary>
    public static class DebriefingStatsCodec
    {
        public const int MaxCasualties = 1000000;
        public const int MaxDamageCost = int.MaxValue;
        public const int MaxBuildings = 100000;
        public const int MaxShots = 1000000;

        public static void WriteDamage<TWriter>(in DebriefingDamageStats stats, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 4);
            KeyedSerializer.WriteField(writer, "cas", stats.Casualties);
            KeyedSerializer.WriteField(writer, "cost", stats.DamageCost);
            KeyedSerializer.WriteField(writer, "dest", stats.BuildingsDestroyed);
            KeyedSerializer.WriteField(writer, "fire", stats.BuildingsOnFire);
        }

        public static void ReadDamage<TReader>(TReader reader, out DebriefingDamageStats stats)
            where TReader : IReader
            => ReadDamage(reader, default, out stats);

        public static void ReadDamage<TReader>(TReader reader, in DebriefingDamageStats current, out DebriefingDamageStats stats)
            where TReader : IReader
        {
            stats = current;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "cas": stats.Casualties = KeyedSerializer.ReadBoundedInt(reader, tag, "cas", 0, MaxCasualties, 0); break;
                    case "cost": stats.DamageCost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, MaxDamageCost, 0); break;
                    case "dest": stats.BuildingsDestroyed = KeyedSerializer.ReadBoundedInt(reader, tag, "dest", 0, MaxBuildings, 0); break;
                    case "fire": stats.BuildingsOnFire = KeyedSerializer.ReadBoundedInt(reader, tag, "fire", 0, MaxBuildings, 0); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
        }

        public static void WriteInfra<TWriter>(in DebriefingInfraStats stats, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "cost", stats.InfrastructureDamageCost);
        }

        public static void ReadInfra<TReader>(TReader reader, out DebriefingInfraStats stats)
            where TReader : IReader
            => ReadInfra(reader, default, out stats);

        public static void ReadInfra<TReader>(TReader reader, in DebriefingInfraStats current, out DebriefingInfraStats stats)
            where TReader : IReader
        {
            stats = current;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "cost": stats.InfrastructureDamageCost = KeyedSerializer.ReadBoundedLong(reader, tag, "cost", 0, long.MaxValue); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
        }

        public static void WriteShot<TWriter>(in DebriefingShotStats stats, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "shots", stats.ShotsFired);
            KeyedSerializer.WriteField(writer, "rnds", stats.RoundsConsumed);
            KeyedSerializer.WriteField(writer, "msls", stats.MissilesConsumed);
        }

        public static void ReadShot<TReader>(TReader reader, out DebriefingShotStats stats)
            where TReader : IReader
            => ReadShot(reader, default, out stats);

        public static void ReadShot<TReader>(TReader reader, in DebriefingShotStats current, out DebriefingShotStats stats)
            where TReader : IReader
        {
            stats = current;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "shots": stats.ShotsFired = KeyedSerializer.ReadBoundedInt(reader, tag, "shots", 0, MaxShots, 0); break;
                    case "rnds": stats.RoundsConsumed = KeyedSerializer.ReadBoundedInt(reader, tag, "rnds", 0, MaxShots, 0); break;
                    case "msls": stats.MissilesConsumed = KeyedSerializer.ReadBoundedInt(reader, tag, "msls", 0, MaxShots, 0); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
        }
    }
}
