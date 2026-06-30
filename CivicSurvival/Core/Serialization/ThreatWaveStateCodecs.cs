using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct OperationalDamagePersistState
    {
        public OperationalDamagePersistState(double gameTime)
            => GameTime = gameTime;

        public double GameTime { get; }
    }

    public static class OperationalDamageCodec
    {
        public static void Write<TWriter>(in OperationalDamagePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "m_GameTime", state.GameTime);
        }

        public static void Read<TReader>(TReader reader, out OperationalDamagePersistState state)
            where TReader : IReader
        {
            double gameTime = 0.0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_GameTime":
                        gameTime = KeyedSerializer.ReadSafeDouble(reader, tag, "m_GameTime", 0.0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new OperationalDamagePersistState(gameTime);
        }
    }

    public readonly struct ThreatSpawnPersistState
    {
        public ThreatSpawnPersistState(
            ulong randomState,
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            bool boundsCached,
            int wavesSinceRecache)
        {
            RandomState = randomState;
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
            BoundsCached = boundsCached;
            WavesSinceRecache = wavesSinceRecache < 0 ? 0 : wavesSinceRecache;
        }

        public ulong RandomState { get; }
        public float MinX { get; }
        public float MinY { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxY { get; }
        public float MaxZ { get; }
        public bool BoundsCached { get; }
        public int WavesSinceRecache { get; }
    }

    public static class ThreatSpawnCodec
    {
        private const int MaxWavesSinceRecache = 10000;

        public static void Write<TWriter>(in ThreatSpawnPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 9);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((long)state.RandomState));
            KeyedSerializer.WriteField(writer, "minX", state.MinX);
            KeyedSerializer.WriteField(writer, "minY", state.MinY);
            KeyedSerializer.WriteField(writer, "minZ", state.MinZ);
            KeyedSerializer.WriteField(writer, "maxX", state.MaxX);
            KeyedSerializer.WriteField(writer, "maxY", state.MaxY);
            KeyedSerializer.WriteField(writer, "maxZ", state.MaxZ);
            KeyedSerializer.WriteField(writer, "boundsCached", state.BoundsCached);
            KeyedSerializer.WriteField(writer, "wavesSinceRecache", state.WavesSinceRecache);
        }

        public static void Read<TReader>(TReader reader, out ThreatSpawnPersistState state)
            where TReader : IReader
        {
            ulong randomState = 0;
            float minX = 0f;
            float minY = 0f;
            float minZ = 0f;
            float maxX = 0f;
            float maxY = 0f;
            float maxZ = 0f;
            bool boundsCached = false;
            int wavesSinceRecache = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "randomState":
                        randomState = ReadRandomState(reader, tag);
                        break;
                    case "minX":
                        minX = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "minX", 0f);
                        break;
                    case "minY":
                        minY = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "minY", 0f);
                        break;
                    case "minZ":
                        minZ = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "minZ", 0f);
                        break;
                    case "maxX":
                        maxX = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "maxX", 0f);
                        break;
                    case "maxY":
                        maxY = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "maxY", 0f);
                        break;
                    case "maxZ":
                        maxZ = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "maxZ", 0f);
                        break;
                    case "boundsCached":
                        boundsCached = KeyedSerializer.ReadBool(reader, tag, "boundsCached");
                        break;
                    case "wavesSinceRecache":
                        wavesSinceRecache = KeyedSerializer.ReadBoundedInt(reader, tag, "wavesSinceRecache", 0, MaxWavesSinceRecache, 0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ThreatSpawnPersistState(
                randomState,
                minX,
                minY,
                minZ,
                maxX,
                maxY,
                maxZ,
                boundsCached,
                wavesSinceRecache);
        }

        private static ulong ReadRandomState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I64, "randomState"))
            {
                return 0;
            }

            reader.Read(out long rawState);
            return unchecked((ulong)rawState);
        }
    }

    public readonly struct WaveExecutorPersistState
    {
        public WaveExecutorPersistState(
            GamePhase phase,
            float timeInPhase,
            float phaseEndTime,
            int waveNumber,
            WaveRole waveRole,
            WaveType waveType,
            int threatsExpected,
            int threatsSpawned,
            bool scenarioStarted)
        {
            Phase = SanitizePhase(phase);
            TimeInPhase = timeInPhase;
            PhaseEndTime = phaseEndTime;
            WaveNumber = waveNumber < 0 ? 0 : waveNumber;
            WaveRole = SanitizeWaveRole(waveRole);
            WaveType = SanitizeWaveType(waveType);
            ThreatsExpected = threatsExpected < 0 ? 0 : threatsExpected;
            ThreatsSpawned = threatsSpawned < 0 ? 0 : threatsSpawned;
            ScenarioStarted = scenarioStarted;
        }

        public GamePhase Phase { get; }
        public float TimeInPhase { get; }
        public float PhaseEndTime { get; }
        public int WaveNumber { get; }
        public WaveRole WaveRole { get; }
        public WaveType WaveType { get; }
        public int ThreatsExpected { get; }
        public int ThreatsSpawned { get; }
        public bool ScenarioStarted { get; }

        private static GamePhase SanitizePhase(GamePhase phase)
            => System.Enum.IsDefined(typeof(GamePhase), phase) ? phase : GamePhase.Calm;

        private static WaveType SanitizeWaveType(WaveType waveType)
            => System.Enum.IsDefined(typeof(WaveType), waveType) ? waveType : WaveType.Harassment;

        private static WaveRole SanitizeWaveRole(WaveRole waveRole)
            => System.Enum.IsDefined(typeof(WaveRole), waveRole) ? waveRole : WaveRole.Regular;
    }

    public static class WaveExecutorCodec
    {
        private const int MaxWaveNumber = 10000;
        private const int MaxThreatCount = 1000;
        private const float DefaultPhaseEndTime = 300f;

        public static void Write<TWriter>(in WaveExecutorPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 9);
            KeyedSerializer.WriteEnumByteField(writer, "phase", (byte)state.Phase);
            KeyedSerializer.WriteField(writer, "timeInPhase", state.TimeInPhase);
            KeyedSerializer.WriteField(writer, "phaseEndTime", state.PhaseEndTime);
            KeyedSerializer.WriteField(writer, "waveNumber", state.WaveNumber);
            KeyedSerializer.WriteEnumByteField(writer, "waveRole", (byte)state.WaveRole);
            KeyedSerializer.WriteEnumByteField(writer, "waveType", (byte)state.WaveType);
            KeyedSerializer.WriteField(writer, "threatsExpected", state.ThreatsExpected);
            KeyedSerializer.WriteField(writer, "threatsSpawned", state.ThreatsSpawned);
            KeyedSerializer.WriteField(writer, "scenarioStarted", state.ScenarioStarted);
        }

        public static void Read<TReader>(TReader reader, out WaveExecutorPersistState state)
            where TReader : IReader
        {
            GamePhase phase = GamePhase.Calm;
            float timeInPhase = 0f;
            float phaseEndTime = 0f;
            int waveNumber = 0;
            WaveRole waveRole = WaveRole.None;
            WaveType waveType = WaveType.Harassment;
            int threatsExpected = 0;
            int threatsSpawned = 0;
            bool scenarioStarted = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "phase":
                        phase = KeyedSerializer.ReadEnumByte<TReader, GamePhase>(reader, tag, "phase", GamePhase.Calm);
                        break;
                    case "timeInPhase":
                        timeInPhase = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "timeInPhase", 0f);
                        break;
                    case "phaseEndTime":
                        phaseEndTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "phaseEndTime", 0f);
                        break;
                    case "waveNumber":
                        waveNumber = KeyedSerializer.ReadMonotonicCounter(reader, tag, "waveNumber", 0, MaxWaveNumber);
                        break;
                    case "waveRole":
                        waveRole = KeyedSerializer.ReadEnumByte<TReader, WaveRole>(reader, tag, "waveRole", WaveRole.None);
                        break;
                    case "waveType":
                        waveType = KeyedSerializer.ReadEnumByte<TReader, WaveType>(reader, tag, "waveType", WaveType.Harassment);
                        break;
                    case "threatsExpected":
                        threatsExpected = KeyedSerializer.ReadClampedInt(reader, tag, "threatsExpected", 0, MaxThreatCount);
                        break;
                    case "threatsSpawned":
                        threatsSpawned = KeyedSerializer.ReadClampedInt(reader, tag, "threatsSpawned", 0, MaxThreatCount);
                        break;
                    case "scenarioStarted":
                        scenarioStarted = KeyedSerializer.ReadBool(reader, tag, "scenarioStarted");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            // LOAD-INVARIANT: executor and scheduler must agree on phase timer recovery.
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

            state = new WaveExecutorPersistState(
                phase,
                timeInPhase,
                phaseEndTime,
                waveNumber,
                waveRole,
                waveType,
                threatsExpected,
                threatsSpawned,
                scenarioStarted);
        }
    }
}
