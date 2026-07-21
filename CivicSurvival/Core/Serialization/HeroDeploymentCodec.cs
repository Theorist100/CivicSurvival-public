using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct HeroDeploymentPersistState
    {
        public HeroDeploymentPersistState(byte heroStatus)
            : this(heroStatus, 0, 0f, 0f, 0f, 0f)
        {
        }

        public HeroDeploymentPersistState(
            byte heroStatus,
            byte archetype,
            float trustDebt,
            float debtEndHour,
            float debtCooldownEndHour,
            float switchCooldownEndHour)
        {
            HeroStatus = heroStatus <= 2 ? heroStatus : (byte)0;
            Archetype = archetype <= 2 ? archetype : (byte)0;
            TrustDebt = trustDebt;
            DebtEndHour = debtEndHour;
            DebtCooldownEndHour = debtCooldownEndHour;
            SwitchCooldownEndHour = switchCooldownEndHour;
        }

        public byte HeroStatus { get; }
        public byte Archetype { get; }
        public float TrustDebt { get; }

        // Absolute game-hours (TotalGameHours, monotonic & continuous across load) — restored
        // verbatim by the consumer, which expires them naturally against the live clock. Unlike
        // Telemarathon's shock (which carries a remaining-hours fallback for the time-unavailable
        // deserialize window), these need no re-anchor: the game-hour clock resumes at the saved
        // value, so a saved deadline stays a valid future deadline.
        public float DebtEndHour { get; }
        public float DebtCooldownEndHour { get; }
        public float SwitchCooldownEndHour { get; }
    }

    public static class HeroDeploymentCodec
    {
        public static void Write<TWriter>(in HeroDeploymentPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 6);
            KeyedSerializer.WriteEnumByteField(writer, "heroStatus", state.HeroStatus);
            KeyedSerializer.WriteEnumByteField(writer, "archetype", state.Archetype);
            KeyedSerializer.WriteField(writer, "trustDebt", state.TrustDebt);
            KeyedSerializer.WriteField(writer, "debtEndHour", state.DebtEndHour);
            KeyedSerializer.WriteField(writer, "debtCdHour", state.DebtCooldownEndHour);
            KeyedSerializer.WriteField(writer, "switchCdHour", state.SwitchCooldownEndHour);
        }

        public static void Read<TReader>(TReader reader, out HeroDeploymentPersistState state)
            where TReader : IReader
        {
            byte heroStatus = 0;
            byte archetype = 0;
            float trustDebt = 0f;
            float debtEndHour = 0f;
            float debtCooldownEndHour = 0f;
            float switchCooldownEndHour = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "heroStatus":
                        heroStatus = ReadStatusByte(reader, tag, "heroStatus");
                        break;
                    case "archetype":
                        archetype = ReadStatusByte(reader, tag, "archetype");
                        break;
                    case "trustDebt":
                        // Non-negative + NaN/Inf-guarded, but no fixed upper cap. The runtime is the
                        // sole cap: AccrueArestovychDebt clamps accrual to the config-tunable
                        // Cognitive.ArestovychDebtMax (default 1.0, runtime: config). A hardcoded 1f
                        // here would silently truncate saved debt the moment a balance patch / remote
                        // config raises ArestovychDebtMax above 1 — same reasoning as ReadSafeHour.
                        trustDebt = KeyedSerializer.ReadSafeFloat(reader, tag, "trustDebt", 0f, float.MaxValue, 0f);
                        break;
                    case "debtEndHour":
                        debtEndHour = ReadSafeHour(reader, tag, "debtEndHour");
                        break;
                    case "debtCdHour":
                        debtCooldownEndHour = ReadSafeHour(reader, tag, "debtCdHour");
                        break;
                    case "switchCdHour":
                        switchCooldownEndHour = ReadSafeHour(reader, tag, "switchCdHour");
                        break;
                    default:
                        // Save-compat: unknown keys (older/newer save) skip; absent keys keep the
                        // local default (archetype=Voice, debt/timers=0) → old saves load clean
                        // without a SaveVersions.GLOBAL bump.
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new HeroDeploymentPersistState(
                heroStatus, archetype, trustDebt, debtEndHour, debtCooldownEndHour, switchCooldownEndHour);
        }

        private static byte ReadStatusByte<TReader>(TReader reader, TypeTag tag, string key)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, key))
                return 0;

            reader.Read(out byte raw);
            return raw <= 2 ? raw : (byte)0;
        }

        // Absolute game-hour timestamps: NaN/Inf/negative-guard only, no upper cap — a timestamp
        // is not a duration, and an arbitrary cap would collapse a legitimate late-game value to 0.
        private static float ReadSafeHour<TReader>(TReader reader, TypeTag tag, string key)
            where TReader : IReader
            => KeyedSerializer.ReadSafeFloat(reader, tag, key, 0f, float.MaxValue, 0f);
    }
}
