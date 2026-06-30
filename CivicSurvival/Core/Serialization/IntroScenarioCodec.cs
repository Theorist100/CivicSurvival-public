using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct IntroChirperStormPersistState
    {
        public IntroChirperStormPersistState(float elapsed, int nextIndex, bool active)
        {
            Elapsed = elapsed;
            NextIndex = nextIndex;
            Active = active;
        }

        public float Elapsed { get; }
        public int NextIndex { get; }
        public bool Active { get; }
    }

    public readonly struct IntroScenarioPersistState
    {
        public IntroScenarioPersistState(
            bool introCompleted,
            bool isIntroPlaying,
            IntroPhase introPhase,
            float introTimer,
            float savedSpeed,
            bool skipIntro,
            bool hasChirperStorm,
            IntroChirperStormPersistState chirperStorm)
        {
            IntroCompleted = introCompleted;
            IsIntroPlaying = isIntroPlaying;
            IntroPhase = introPhase;
            IntroTimer = introTimer;
            SavedSpeed = savedSpeed;
            SkipIntro = skipIntro;
            HasChirperStorm = hasChirperStorm;
            ChirperStorm = chirperStorm;
        }

        public bool IntroCompleted { get; }
        public bool IsIntroPlaying { get; }
        public IntroPhase IntroPhase { get; }
        public float IntroTimer { get; }
        public float SavedSpeed { get; }
        public bool SkipIntro { get; }
        public bool HasChirperStorm { get; }
        public IntroChirperStormPersistState ChirperStorm { get; }
    }

    public static class IntroScenarioCodec
    {
        private const float MaxIntroTimerSeconds = 600f;
        private const float MaxGameSpeed = 16f;
        private const float MaxChirperStormDurationSeconds = 100f;

        public static void Write<TWriter>(in IntroScenarioPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 7);
            KeyedSerializer.WriteField(writer, "introCompleted", state.IntroCompleted);
            KeyedSerializer.WriteField(writer, "isIntroPlaying", state.IsIntroPlaying);
            KeyedSerializer.WriteEnumByteField(writer, "introPhase", (byte)state.IntroPhase);
            KeyedSerializer.WriteField(writer, "introTimer", state.IntroTimer);
            KeyedSerializer.WriteField(writer, "savedSpeed", state.SavedSpeed);
            KeyedSerializer.WriteField(writer, "skipIntro", state.SkipIntro);
            KeyedSerializer.WriteBufferHeader(writer, "chirperStorm", state.HasChirperStorm ? 1 : 0);

            if (state.HasChirperStorm)
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "elapsed", state.ChirperStorm.Elapsed);
                KeyedSerializer.WriteField(writer, "nextIndex", state.ChirperStorm.NextIndex);
                KeyedSerializer.WriteField(writer, "active", state.ChirperStorm.Active);
            }
        }

        public static void Read<TReader>(TReader reader, out IntroScenarioPersistState state)
            where TReader : IReader
        {
            bool introCompleted = false;
            bool isIntroPlaying = false;
            var introPhase = IntroPhase.None;
            float introTimer = 0f;
            float savedSpeed = 1f;
            bool skipIntro = false;
            bool hasChirperStorm = false;
            var chirperStorm = default(IntroChirperStormPersistState);

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "introCompleted": introCompleted = KeyedSerializer.ReadBool(reader, tag, "introCompleted"); break;
                    case "isIntroPlaying": isIntroPlaying = KeyedSerializer.ReadBool(reader, tag, "isIntroPlaying"); break;
                    case "introPhase": introPhase = KeyedSerializer.ReadEnumByte<TReader, IntroPhase>(reader, tag, "introPhase", IntroPhase.None); break;
                    case "introTimer": introTimer = KeyedSerializer.ReadSafeFloat(reader, tag, "introTimer", 0f, MaxIntroTimerSeconds, 0f); break;
                    case "savedSpeed": savedSpeed = KeyedSerializer.ReadSafeFloat(reader, tag, "savedSpeed", 0f, MaxGameSpeed, 1f); break;
                    case "introModalShown":
                        KeyedSerializer.Skip(reader, tag);
                        break;
                    case "skipIntro": skipIntro = KeyedSerializer.ReadBool(reader, tag, "skipIntro"); break;
                    case "chirperStorm":
                        hasChirperStorm = TryReadChirperStorm(reader, tag, out chirperStorm);
                        break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new IntroScenarioPersistState(
                introCompleted,
                isIntroPlaying,
                introPhase,
                introTimer,
                savedSpeed,
                skipIntro,
                hasChirperStorm,
                chirperStorm);
        }

        private static bool TryReadChirperStorm<TReader>(
            TReader reader,
            TypeTag tag,
            out IntroChirperStormPersistState state)
            where TReader : IReader
        {
            state = default;
            int stormCount = KeyedSerializer.ReadBufferCount(reader, tag, "chirperStorm", 1);
            if (stormCount <= 0)
            {
                return false;
            }

            float elapsed = 0f;
            int nextIndex = 0;
            bool active = false;
            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
#pragma warning disable CIVIC135 // Three-field nested keyed payload; switch mirrors surrounding codec style.
                switch (fieldKey)
                {
                    case "elapsed":
                        elapsed = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "elapsed", 0f, MaxChirperStormDurationSeconds, 0f);
                        break;
                    case "nextIndex":
                        nextIndex = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "nextIndex", 0, 1000, 0);
                        break;
                    case "active":
                        active = KeyedSerializer.ReadBool(reader, fieldTag, "active");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, fieldTag);
                        break;
                }
#pragma warning restore CIVIC135
            }

            state = new IntroChirperStormPersistState(elapsed, nextIndex, active);
            return true;
        }
    }
}
