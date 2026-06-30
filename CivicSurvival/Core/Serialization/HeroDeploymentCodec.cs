using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct HeroDeploymentPersistState
    {
        public HeroDeploymentPersistState(byte heroStatus)
            => HeroStatus = heroStatus <= 2 ? heroStatus : (byte)0;

        public byte HeroStatus { get; }
    }

    public static class HeroDeploymentCodec
    {
        public static void Write<TWriter>(in HeroDeploymentPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteEnumByteField(writer, "heroStatus", state.HeroStatus);
        }

        public static void Read<TReader>(TReader reader, out HeroDeploymentPersistState state)
            where TReader : IReader
        {
            byte heroStatus = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "heroStatus":
                        heroStatus = ReadHeroStatus(reader, tag);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new HeroDeploymentPersistState(heroStatus);
        }

        private static byte ReadHeroStatus<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, "heroStatus"))
                return 0;

            reader.Read(out byte raw);
            return raw <= 2 ? raw : (byte)0;
        }
    }
}
