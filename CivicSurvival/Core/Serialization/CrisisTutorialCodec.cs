using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CrisisTutorialPersistState
    {
        public CrisisTutorialPersistState(
            bool firstStrikeShown,
            bool exodusWarningShown,
            bool gridTabOpenedInCrisis,
            bool shadowTabOpenedInCrisis,
            bool gridTabOpenedPreCrisis,
            bool shadowTabOpenedPreCrisis,
            bool crisisActActive,
            int crisisStartDay,
            int populationAtCrisisStart,
            bool firstWaveEnded,
            bool firstWaveCausedDamage)
        {
            FirstStrikeShown = firstStrikeShown;
            ExodusWarningShown = exodusWarningShown;
            GridTabOpenedInCrisis = gridTabOpenedInCrisis;
            ShadowTabOpenedInCrisis = shadowTabOpenedInCrisis;
            GridTabOpenedPreCrisis = gridTabOpenedPreCrisis;
            ShadowTabOpenedPreCrisis = shadowTabOpenedPreCrisis;
            CrisisActActive = crisisActActive;
            CrisisStartDay = crisisStartDay;
            PopulationAtCrisisStart = populationAtCrisisStart;
            FirstWaveEnded = firstWaveEnded;
            FirstWaveCausedDamage = firstWaveCausedDamage;
        }

        public bool FirstStrikeShown { get; }
        public bool ExodusWarningShown { get; }
        public bool GridTabOpenedInCrisis { get; }
        public bool ShadowTabOpenedInCrisis { get; }
        public bool GridTabOpenedPreCrisis { get; }
        public bool ShadowTabOpenedPreCrisis { get; }
        public bool CrisisActActive { get; }
        public int CrisisStartDay { get; }
        public int PopulationAtCrisisStart { get; }
        public bool FirstWaveEnded { get; }
        public bool FirstWaveCausedDamage { get; }
    }

    public static class CrisisTutorialCodec
    {
        public static void Write<TWriter>(in CrisisTutorialPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 11);
            KeyedSerializer.WriteField(writer, "m_FirstStrikeShown", state.FirstStrikeShown);
            KeyedSerializer.WriteField(writer, "m_ExodusWarningShown", state.ExodusWarningShown);
            KeyedSerializer.WriteField(writer, "m_GridTabOpenedInCrisis", state.GridTabOpenedInCrisis);
            KeyedSerializer.WriteField(writer, "m_ShadowTabOpenedInCrisis", state.ShadowTabOpenedInCrisis);
            KeyedSerializer.WriteField(writer, "m_GridTabOpenedPreCrisis", state.GridTabOpenedPreCrisis);
            KeyedSerializer.WriteField(writer, "m_ShadowTabOpenedPreCrisis", state.ShadowTabOpenedPreCrisis);
            KeyedSerializer.WriteField(writer, "m_CrisisActActive", state.CrisisActActive);
            KeyedSerializer.WriteField(writer, "m_CrisisStartDay", state.CrisisStartDay);
            KeyedSerializer.WriteField(writer, "m_PopulationAtCrisisStart", state.PopulationAtCrisisStart);
            KeyedSerializer.WriteField(writer, "m_FirstWaveEnded", state.FirstWaveEnded);
            KeyedSerializer.WriteField(writer, "m_FirstWaveCausedDamage", state.FirstWaveCausedDamage);
        }

        public static void Read<TReader>(TReader reader, out CrisisTutorialPersistState state)
            where TReader : IReader
        {
            bool firstStrikeShown = false;
            bool exodusWarningShown = false;
            bool gridTabOpenedInCrisis = false;
            bool shadowTabOpenedInCrisis = false;
            bool gridTabOpenedPreCrisis = false;
            bool shadowTabOpenedPreCrisis = false;
            bool crisisActActive = false;
            int crisisStartDay = 0;
            int populationAtCrisisStart = 0;
            bool firstWaveEnded = false;
            bool firstWaveCausedDamage = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_FirstStrikeShown": firstStrikeShown = KeyedSerializer.ReadBool(reader, tag, "m_FirstStrikeShown"); break;
                    case "m_ExodusWarningShown": exodusWarningShown = KeyedSerializer.ReadBool(reader, tag, "m_ExodusWarningShown"); break;
                    case "m_GridTabOpenedInCrisis": gridTabOpenedInCrisis = KeyedSerializer.ReadBool(reader, tag, "m_GridTabOpenedInCrisis"); break;
                    case "m_ShadowTabOpenedInCrisis": shadowTabOpenedInCrisis = KeyedSerializer.ReadBool(reader, tag, "m_ShadowTabOpenedInCrisis"); break;
                    case "m_GridTabOpenedPreCrisis": gridTabOpenedPreCrisis = KeyedSerializer.ReadBool(reader, tag, "m_GridTabOpenedPreCrisis"); break;
                    case "m_ShadowTabOpenedPreCrisis": shadowTabOpenedPreCrisis = KeyedSerializer.ReadBool(reader, tag, "m_ShadowTabOpenedPreCrisis"); break;
                    case "m_CrisisActActive": crisisActActive = KeyedSerializer.ReadBool(reader, tag, "m_CrisisActActive"); break;
                    case "m_CrisisStartDay": crisisStartDay = KeyedSerializer.ReadInt(reader, tag, "m_CrisisStartDay"); break;
                    case "m_PopulationAtCrisisStart": populationAtCrisisStart = KeyedSerializer.ReadInt(reader, tag, "m_PopulationAtCrisisStart"); break;
                    case "m_FirstWaveEnded": firstWaveEnded = KeyedSerializer.ReadBool(reader, tag, "m_FirstWaveEnded"); break;
                    case "m_FirstWaveCausedDamage": firstWaveCausedDamage = KeyedSerializer.ReadBool(reader, tag, "m_FirstWaveCausedDamage"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new CrisisTutorialPersistState(
                firstStrikeShown,
                exodusWarningShown,
                gridTabOpenedInCrisis,
                shadowTabOpenedInCrisis,
                gridTabOpenedPreCrisis,
                shadowTabOpenedPreCrisis,
                crisisActActive,
                crisisStartDay,
                populationAtCrisisStart,
                firstWaveEnded,
                firstWaveCausedDamage);
        }
    }
}
