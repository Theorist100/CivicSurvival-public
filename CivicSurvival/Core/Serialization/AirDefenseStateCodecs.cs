using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct SerializableRandomState
    {
        public SerializableRandomState(ulong state)
            => State = state == 0UL ? 1UL : state;

        public ulong State { get; }
    }

    public static class SerializableRandomCodec
    {
        public static void Write<TWriter>(in SerializableRandomState state, TWriter writer)
            where TWriter : IWriter
            => writer.Write(state.State);

        public static void Read<TReader>(TReader reader, out SerializableRandomState state)
            where TReader : IReader
        {
            reader.Read(out ulong rawState);
            state = new SerializableRandomState(rawState);
        }
    }

    public readonly struct AirDefensePolicyState
    {
        public AirDefensePolicyState(DefensePolicy policy)
            => Policy = Sanitize(policy);

        public DefensePolicy Policy { get; }

        public static DefensePolicy Sanitize(DefensePolicy policy)
            => System.Enum.IsDefined(typeof(DefensePolicy), policy)
                ? policy
                : DefensePolicy.HumanitarianShield;
    }

    public static class AirDefensePolicyCodec
    {
        public static void Write<TWriter>(in AirDefensePolicyState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteEnumByteField(writer, "m_CurrentPolicy", (byte)state.Policy);
        }

        public static void Read<TReader>(TReader reader, out AirDefensePolicyState state)
            where TReader : IReader
        {
            DefensePolicy policy = DefensePolicy.HumanitarianShield;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_CurrentPolicy":
                        policy = KeyedSerializer.ReadEnumByte<TReader, DefensePolicy>(
                            reader,
                            tag,
                            "m_CurrentPolicy",
                            DefensePolicy.HumanitarianShield);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new AirDefensePolicyState(policy);
        }
    }

    public readonly struct AirDefenseCreditsPersistState
    {
        public AirDefenseCreditsPersistState(
            int heritageCredits,
            int heritageCreditsMax,
            int donorPatriotCredits,
            int donorPatriotCreditsMax,
            bool patriotInterceptsDrones,
            bool autoResupplyEnabled,
            float lastResupplyHourHeritage,
            float lastResupplyHourBofors,
            float lastResupplyHourGepard,
            float lastResupplyHourPatriot,
            int lastResupplyWavePatriot)
        {
            HeritageCredits = SanitizeCredit(heritageCredits);
            HeritageCreditsMax = SanitizeCredit(heritageCreditsMax);
            DonorPatriotCredits = SanitizeCredit(donorPatriotCredits);
            DonorPatriotCreditsMax = SanitizeCredit(donorPatriotCreditsMax);
            PatriotInterceptsDrones = patriotInterceptsDrones;
            AutoResupplyEnabled = autoResupplyEnabled;
            LastResupplyHourHeritage = SanitizeHour(lastResupplyHourHeritage);
            LastResupplyHourBofors = SanitizeHour(lastResupplyHourBofors);
            LastResupplyHourGepard = SanitizeHour(lastResupplyHourGepard);
            LastResupplyHourPatriot = SanitizeHour(lastResupplyHourPatriot);
            LastResupplyWavePatriot = SanitizeWave(lastResupplyWavePatriot);
        }

        public int HeritageCredits { get; }
        public int HeritageCreditsMax { get; }
        public int DonorPatriotCredits { get; }
        public int DonorPatriotCreditsMax { get; }
        public bool PatriotInterceptsDrones { get; }
        public bool AutoResupplyEnabled { get; }
        public float LastResupplyHourHeritage { get; }
        public float LastResupplyHourBofors { get; }
        public float LastResupplyHourGepard { get; }
        public float LastResupplyHourPatriot { get; }
        public int LastResupplyWavePatriot { get; }

        /// <summary>
        /// Upper bound for any single AirDefense credit pool serialized through
        /// this codec — guards against pathological values from save tampering
        /// or earlier-version overflow. Picked well above realistic mid-game
        /// totals (a few hundred to a few thousand) with headroom for late-game.
        /// </summary>
        private const int MaxCreditValue = 100_000;

        private static int SanitizeCredit(int value)
        {
            if (value < 0)
                return 0;
            if (value > MaxCreditValue)
                return MaxCreditValue;
            return value;
        }

        // Last-resupply game-hour. -1 (NoResupplyHour) means "never resupplied" → not on cooldown.
        // NaN/Inf from a tampered save collapse to the safe sentinel; negatives other than the
        // sentinel also collapse (a real timestamp is >= 0).
        private static float SanitizeHour(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return -1f;
            return value;
        }

        // Last-resupply wave number. -1 (NoResupplyWave) means "never resupplied" → not on wave
        // cooldown. Any negative other than the sentinel collapses to it (a real wave number is
        // >= 0); the upper bound guards a tampered save from an absurd value.
        private static int SanitizeWave(int value)
        {
            if (value < 0)
                return -1;
            if (value > MaxCreditValue)
                return MaxCreditValue;
            return value;
        }
    }

    public static class AirDefenseCreditsCodec
    {
        public static void Write<TWriter>(in AirDefenseCreditsPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 11);
            KeyedSerializer.WriteField(writer, "hCr", state.HeritageCredits);
            KeyedSerializer.WriteField(writer, "hMax", state.HeritageCreditsMax);
            KeyedSerializer.WriteField(writer, "dCr", state.DonorPatriotCredits);
            KeyedSerializer.WriteField(writer, "dMax", state.DonorPatriotCreditsMax);
            KeyedSerializer.WriteField(writer, "pidn", state.PatriotInterceptsDrones);
            KeyedSerializer.WriteField(writer, "auto", state.AutoResupplyEnabled);
            KeyedSerializer.WriteField(writer, "rsH", state.LastResupplyHourHeritage);
            KeyedSerializer.WriteField(writer, "rsB", state.LastResupplyHourBofors);
            KeyedSerializer.WriteField(writer, "rsG", state.LastResupplyHourGepard);
            KeyedSerializer.WriteField(writer, "rsP", state.LastResupplyHourPatriot);
            KeyedSerializer.WriteField(writer, "rwP", state.LastResupplyWavePatriot);
        }

        public static void Read<TReader>(TReader reader, out AirDefenseCreditsPersistState state)
            where TReader : IReader
        {
            int heritageCredits = 0;
            int heritageCreditsMax = 0;
            int donorPatriotCredits = 0;
            int donorPatriotCreditsMax = 0;
            // Default OFF: a legacy save written before this field exists has no "pidn"
            // key, so the player setting reads back as false (Patriot reserved for ballistics).
            bool patriotInterceptsDrones = false;
            // Default ON: a legacy save written before this field exists has no "auto" key, so the
            // per-save auto-resupply rule reads back as true (matches the prior always-on behavior).
            bool autoResupplyEnabled = true;
            // Default NoResupplyHour (-1): a legacy save lacks these keys → "never resupplied",
            // so no type starts mid-cooldown after loading a pre-cooldown save.
            float lastResupplyHourHeritage = -1f;
            float lastResupplyHourBofors = -1f;
            float lastResupplyHourGepard = -1f;
            float lastResupplyHourPatriot = -1f;
            // Default NoResupplyWave (-1): a legacy save lacks this key → "never resupplied",
            // so Patriot does not start mid-wave-cooldown after loading a pre-wave-gate save.
            int lastResupplyWavePatriot = -1;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "hCr":
                        heritageCredits = KeyedSerializer.ReadBoundedInt(reader, tag, "hCr", 0, 100000, 0);
                        break;
                    case "hMax":
                        heritageCreditsMax = KeyedSerializer.ReadBoundedInt(reader, tag, "hMax", 0, 100000, 0);
                        break;
                    case "dCr":
                        donorPatriotCredits = KeyedSerializer.ReadBoundedInt(reader, tag, "dCr", 0, 100000, 0);
                        break;
                    case "dMax":
                        donorPatriotCreditsMax = KeyedSerializer.ReadBoundedInt(reader, tag, "dMax", 0, 100000, 0);
                        break;
                    case "pidn":
                        patriotInterceptsDrones = KeyedSerializer.ReadBool(reader, tag, "pidn", false);
                        break;
                    case "auto":
                        autoResupplyEnabled = KeyedSerializer.ReadBool(reader, tag, "auto", true);
                        break;
                    case "rsH":
                        lastResupplyHourHeritage = KeyedSerializer.ReadFloat(reader, tag, "rsH", -1f);
                        break;
                    case "rsB":
                        lastResupplyHourBofors = KeyedSerializer.ReadFloat(reader, tag, "rsB", -1f);
                        break;
                    case "rsG":
                        lastResupplyHourGepard = KeyedSerializer.ReadFloat(reader, tag, "rsG", -1f);
                        break;
                    case "rsP":
                        lastResupplyHourPatriot = KeyedSerializer.ReadFloat(reader, tag, "rsP", -1f);
                        break;
                    case "rwP":
                        lastResupplyWavePatriot = KeyedSerializer.ReadBoundedInt(reader, tag, "rwP", -1, 100000, -1);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new AirDefenseCreditsPersistState(
                heritageCredits,
                heritageCreditsMax,
                donorPatriotCredits,
                donorPatriotCreditsMax,
                patriotInterceptsDrones,
                autoResupplyEnabled,
                lastResupplyHourHeritage,
                lastResupplyHourBofors,
                lastResupplyHourGepard,
                lastResupplyHourPatriot,
                lastResupplyWavePatriot);
        }
    }

    public static class EmptyPayloadCodec
    {
        public static void Write<TWriter>(TWriter writer)
            where TWriter : IWriter
        {
            // Intentionally empty: this codec represents version-only save blocks.
        }

        public static void Read<TReader>(TReader reader)
            where TReader : IReader
        {
            // Intentionally empty: this codec represents version-only save blocks.
        }
    }
}
