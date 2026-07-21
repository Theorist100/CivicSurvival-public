using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct OminousSignsPersistState
    {
        public OminousSignsPersistState(
            bool active,
            OminousSignFlags signsTriggered,
            bool warStarted)
        {
            Active = active;
            SignsTriggered = signsTriggered;
            WarStarted = warStarted;
        }

        public bool Active { get; }
        public OminousSignFlags SignsTriggered { get; }
        public bool WarStarted { get; }
    }

    public static class OminousSignsCodec
    {
        private const byte AllOminousSignsMask = 0x7F;

        public static void Write<TWriter>(in OminousSignsPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "m_Active", state.Active);
            KeyedSerializer.WriteEnumByteField(writer, "m_SignsTriggered", (byte)state.SignsTriggered);
            KeyedSerializer.WriteField(writer, "m_WarStarted", state.WarStarted);
        }

        public static void Read<TReader>(TReader reader, out OminousSignsPersistState state)
            where TReader : IReader
        {
            bool active = false;
            var signsTriggered = default(OminousSignFlags);
            bool warStarted = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_Active":
                        active = KeyedSerializer.ReadBool(reader, tag, "m_Active");
                        break;
                    case "m_SignsTriggered":
                        if (KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, "m_SignsTriggered"))
                        {
                            reader.Read(out byte rawSigns);
                            signsTriggered = ToOminousSigns(rawSigns);
                        }
                        break;
                    case "m_WarStarted":
                        warStarted = KeyedSerializer.ReadBool(reader, tag, "m_WarStarted");
                        break;
                    default:
                        // Legacy fields from superseded models are intentionally skipped:
                        // day-countdown (m_CurrentWarDay, m_WarStartRolled, m_ActivationGameDay)
                        // and population-radar (m_OriginalPopulation, m_RadarThreshold).
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new OminousSignsPersistState(
                active,
                signsTriggered,
                warStarted);
        }

        private static OminousSignFlags ToOminousSigns(byte raw)
#pragma warning disable CIVIC140 // OminousSignFlags is a [Flags] bitmask; unknown bits are stripped before the cast.
            => (OminousSignFlags)(raw & AllOminousSignsMask);
#pragma warning restore CIVIC140
    }
}
