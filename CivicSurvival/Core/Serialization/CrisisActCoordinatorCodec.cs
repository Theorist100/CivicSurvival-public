using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CrisisActCoordinatorPersistState
    {
        public CrisisActCoordinatorPersistState(
            bool crisisActive,
            int crisisStartDay,
            bool hasCrisisStartDay,
            int currentDay,
            int wavesSurvived,
            int introWaveThreatCount,
            bool hasPendingTransition,
            Act previousAct,
            bool bankingChirpSent,
            int exodusPendingSinceDay,
            bool hasExodusPendingSince)
        {
            CrisisActive = crisisActive;
            CrisisStartDay = crisisStartDay;
            HasCrisisStartDay = hasCrisisStartDay;
            CurrentDay = currentDay;
            WavesSurvived = wavesSurvived;
            IntroWaveThreatCount = introWaveThreatCount;
            HasPendingTransition = hasPendingTransition;
            PreviousAct = previousAct;
            BankingChirpSent = bankingChirpSent;
            ExodusPendingSinceDay = exodusPendingSinceDay;
            HasExodusPendingSince = hasExodusPendingSince;
        }

        public bool CrisisActive { get; }
        public int CrisisStartDay { get; }
        public bool HasCrisisStartDay { get; }
        public int CurrentDay { get; }
        public int WavesSurvived { get; }
        public int IntroWaveThreatCount { get; }
        public bool HasPendingTransition { get; }
        public Act PreviousAct { get; }
        public bool BankingChirpSent { get; }
        /// <summary>Game day on which the exodus condition was first observed.</summary>
        public int ExodusPendingSinceDay { get; }
        /// <summary>Whether an exodus observation is waiting for the day boundary.</summary>
        public bool HasExodusPendingSince { get; }
    }

    public static class CrisisActCoordinatorCodec
    {
        public static void Write<TWriter>(in CrisisActCoordinatorPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 11);
            KeyedSerializer.WriteField(writer, "m_CrisisActive", state.CrisisActive);
            KeyedSerializer.WriteField(writer, "m_CrisisStartDay", state.CrisisStartDay);
            KeyedSerializer.WriteField(writer, "m_HasCrisisStartDay", state.HasCrisisStartDay);
            KeyedSerializer.WriteField(writer, "m_CurrentDay", state.CurrentDay);
            KeyedSerializer.WriteField(writer, "m_WavesSurvived", state.WavesSurvived);
            KeyedSerializer.WriteField(writer, "m_IntroWaveThreatCount", state.IntroWaveThreatCount);
            KeyedSerializer.WriteField(writer, "m_HasPendingTransition", state.HasPendingTransition);
            KeyedSerializer.WriteEnumByteField(writer, "m_PreviousAct", (byte)state.PreviousAct);
            KeyedSerializer.WriteField(writer, "m_BankingChirpSent", state.BankingChirpSent);
            KeyedSerializer.WriteField(writer, "m_ExodusPendingSinceDay", state.ExodusPendingSinceDay);
            KeyedSerializer.WriteField(writer, "m_HasExodusPendingSince", state.HasExodusPendingSince);
        }

        public static void Read<TReader>(TReader reader, out CrisisActCoordinatorPersistState state)
            where TReader : IReader
        {
            bool crisisActive = false;
            int crisisStartDay = 0;
            bool hasCrisisStartDay = false;
            bool sawHasCrisisStartDay = false;
            int currentDay = 0;
            int wavesSurvived = 0;
            int introWaveThreatCount = 0;
            bool hasPendingTransition = false;
            var previousAct = default(Act);
            bool bankingChirpSent = false;
            int exodusPendingSinceDay = 0;
            bool hasExodusPendingSince = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_CrisisActive": crisisActive = KeyedSerializer.ReadBool(reader, tag, "m_CrisisActive"); break;
                    case "m_CrisisStartDay": crisisStartDay = KeyedSerializer.ReadInt(reader, tag, "m_CrisisStartDay"); break;
                    case "m_HasCrisisStartDay":
                        hasCrisisStartDay = KeyedSerializer.ReadBool(reader, tag, "m_HasCrisisStartDay");
                        sawHasCrisisStartDay = true;
                        break;
                    case "m_CurrentDay": currentDay = KeyedSerializer.ReadInt(reader, tag, "m_CurrentDay"); break;
                    case "m_WavesSurvived": wavesSurvived = KeyedSerializer.ReadInt(reader, tag, "m_WavesSurvived"); break;
                    case "m_IntroWaveThreatCount": introWaveThreatCount = KeyedSerializer.ReadInt(reader, tag, "m_IntroWaveThreatCount"); break;
                    case "m_HasPendingTransition": hasPendingTransition = KeyedSerializer.ReadBool(reader, tag, "m_HasPendingTransition"); break;
                    case "m_PreviousAct": previousAct = KeyedSerializer.ReadEnumByte<TReader, Act>(reader, tag, "m_PreviousAct", default); break;
                    case "m_BankingChirpSent": bankingChirpSent = KeyedSerializer.ReadBool(reader, tag, "m_BankingChirpSent"); break;
                    case "m_ExodusPendingSinceDay": exodusPendingSinceDay = KeyedSerializer.ReadInt(reader, tag, "m_ExodusPendingSinceDay"); break;
                    case "m_HasExodusPendingSince": hasExodusPendingSince = KeyedSerializer.ReadBool(reader, tag, "m_HasExodusPendingSince"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            if (!sawHasCrisisStartDay)
                hasCrisisStartDay = crisisActive || crisisStartDay > 0;

            state = new CrisisActCoordinatorPersistState(
                crisisActive,
                crisisStartDay,
                hasCrisisStartDay,
                currentDay,
                wavesSurvived,
                introWaveThreatCount,
                hasPendingTransition,
                previousAct,
                bankingChirpSent,
                exodusPendingSinceDay,
                hasExodusPendingSince);
        }
    }
}
