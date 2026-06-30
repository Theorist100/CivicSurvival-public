using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct WaveSchedulerPersistState
    {
        public WaveSchedulerPersistState(
            GamePhase phase,
            float timeInPhase,
            float phaseEndTime,
            int waveNumber,
            WaveRole waveRole,
            WaveType waveType,
            int threatsExpected,
            uint randomState,
            bool scenarioStarted,
            bool warStartedReceived,
            bool introAttackFired)
        {
            Phase = phase;
            TimeInPhase = timeInPhase;
            PhaseEndTime = phaseEndTime;
            WaveNumber = waveNumber;
            WaveRole = waveRole;
            WaveType = waveType;
            ThreatsExpected = threatsExpected;
            RandomState = randomState;
            ScenarioStarted = scenarioStarted;
            WarStartedReceived = warStartedReceived;
            IntroAttackFired = introAttackFired;
        }

        public GamePhase Phase { get; }
        public float TimeInPhase { get; }
        public float PhaseEndTime { get; }
        public int WaveNumber { get; }
        public WaveRole WaveRole { get; }
        public WaveType WaveType { get; }
        public int ThreatsExpected { get; }
        public uint RandomState { get; }
        public bool ScenarioStarted { get; }
        public bool WarStartedReceived { get; }
        public bool IntroAttackFired { get; }
    }

    public static class WaveSchedulerCodec
    {
        private const float DefaultPhaseEndTime = 300f;

        public static void Write<TWriter>(in WaveSchedulerPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 11);
            KeyedSerializer.WriteEnumByteField(writer, "phase", (byte)state.Phase);
            KeyedSerializer.WriteField(writer, "timeInPhase", state.TimeInPhase);
            KeyedSerializer.WriteField(writer, "phaseEndTime", state.PhaseEndTime);
            KeyedSerializer.WriteField(writer, "waveNumber", state.WaveNumber);
            KeyedSerializer.WriteEnumByteField(writer, "waveRole", (byte)state.WaveRole);
            KeyedSerializer.WriteEnumByteField(writer, "waveType", (byte)state.WaveType);
            KeyedSerializer.WriteField(writer, "threatsExpected", state.ThreatsExpected);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((int)state.RandomState));
            KeyedSerializer.WriteField(writer, "scenarioStarted", state.ScenarioStarted);
            KeyedSerializer.WriteField(writer, "warStartedReceived", state.WarStartedReceived);
            KeyedSerializer.WriteField(writer, "introAttackFired", state.IntroAttackFired);
        }

        public static void Read<TReader>(TReader reader, out WaveSchedulerPersistState state)
            where TReader : IReader
        {
            var phase = GamePhase.Calm;
            float timeInPhase = 0f;
            float phaseEndTime = 0f;
            int waveNumber = 0;
            var waveRole = WaveRole.None;
            var waveType = WaveType.Harassment;
            int threatsExpected = 0;
            uint randomState = 0;
            bool scenarioStarted = false;
            bool warStartedReceived = false;
            bool introAttackFired = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "phase": phase = KeyedSerializer.ReadEnumByte<TReader, GamePhase>(reader, tag, "phase", GamePhase.Calm); break;
                    case "timeInPhase": timeInPhase = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "timeInPhase", 0f); break;
                    case "phaseEndTime": phaseEndTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "phaseEndTime", 0f); break;
                    case "waveNumber": waveNumber = KeyedSerializer.ReadMonotonicCounter(reader, tag, "waveNumber", 0, 10000); break;
                    case "waveRole": waveRole = KeyedSerializer.ReadEnumByte<TReader, WaveRole>(reader, tag, "waveRole", WaveRole.None); break;
                    case "waveType": waveType = KeyedSerializer.ReadEnumByte<TReader, WaveType>(reader, tag, "waveType", WaveType.Harassment); break;
                    case "threatsExpected": threatsExpected = KeyedSerializer.ReadClampedInt(reader, tag, "threatsExpected", 0, 1000); break;
                    case "randomState":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "randomState"))
                        {
                            break;
                        }
                        reader.Read(out int rawRandomState);
                        randomState = unchecked((uint)rawRandomState);
                        break;
                    case "scenarioStarted": scenarioStarted = KeyedSerializer.ReadBool(reader, tag, "scenarioStarted"); break;
                    case "warStartedReceived": warStartedReceived = KeyedSerializer.ReadBool(reader, tag, "warStartedReceived"); break;
                    case "introAttackFired": introAttackFired = KeyedSerializer.ReadBool(reader, tag, "introAttackFired"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            if (float.IsNaN(timeInPhase) || float.IsInfinity(timeInPhase) || timeInPhase < 0f)
            {
                timeInPhase = 0f;
            }
            if (float.IsNaN(phaseEndTime) || float.IsInfinity(phaseEndTime) || phaseEndTime < 0f)
            {
                phaseEndTime = DefaultPhaseEndTime;
            }
            if (timeInPhase > phaseEndTime)
            {
                timeInPhase = phaseEndTime;
            }

            state = new WaveSchedulerPersistState(
                phase,
                timeInPhase,
                phaseEndTime,
                waveNumber,
                waveRole,
                waveType,
                threatsExpected,
                randomState,
                scenarioStarted,
                warStartedReceived,
                introAttackFired);
        }
    }
}
