using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CrisisTutorialPersistState
    {
        public CrisisTutorialPersistState(
            bool firstStrikeShown,
            bool exodusWarningShown,
            int tabsOpenedInCrisis,
            int tabsOpenedPreCrisis,
            bool crisisActActive,
            int crisisStartDay,
            bool firstWaveEnded,
            bool firstWaveCausedDamage)
        {
            FirstStrikeShown = firstStrikeShown;
            ExodusWarningShown = exodusWarningShown;
            TabsOpenedInCrisis = tabsOpenedInCrisis;
            TabsOpenedPreCrisis = tabsOpenedPreCrisis;
            CrisisActActive = crisisActActive;
            CrisisStartDay = crisisStartDay;
            FirstWaveEnded = firstWaveEnded;
            FirstWaveCausedDamage = firstWaveCausedDamage;
        }

        public bool FirstStrikeShown { get; }
        public bool ExodusWarningShown { get; }

        // CrisisTab bitmasks (GRID/SHADOW/WAR/RADAR/DEFENSE). Replaced the four
        // per-tab bools; keyed serialization skips the old field names cleanly, so
        // a pre-existing save simply loads the masks as 0 (pre-release, no migration).
        public int TabsOpenedInCrisis { get; }
        public int TabsOpenedPreCrisis { get; }

        public bool CrisisActActive { get; }
        public int CrisisStartDay { get; }
        public bool FirstWaveEnded { get; }
        public bool FirstWaveCausedDamage { get; }
    }

    public static class CrisisTutorialCodec
    {
        public static void Write<TWriter>(in CrisisTutorialPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteField(writer, "m_FirstStrikeShown", state.FirstStrikeShown);
            KeyedSerializer.WriteField(writer, "m_ExodusWarningShown", state.ExodusWarningShown);
            KeyedSerializer.WriteField(writer, "m_TabsOpenedInCrisis", state.TabsOpenedInCrisis);
            KeyedSerializer.WriteField(writer, "m_TabsOpenedPreCrisis", state.TabsOpenedPreCrisis);
            KeyedSerializer.WriteField(writer, "m_CrisisActActive", state.CrisisActActive);
            KeyedSerializer.WriteField(writer, "m_CrisisStartDay", state.CrisisStartDay);
            KeyedSerializer.WriteField(writer, "m_FirstWaveEnded", state.FirstWaveEnded);
            KeyedSerializer.WriteField(writer, "m_FirstWaveCausedDamage", state.FirstWaveCausedDamage);
        }

        public static void Read<TReader>(TReader reader, out CrisisTutorialPersistState state)
            where TReader : IReader
        {
            bool firstStrikeShown = false;
            bool exodusWarningShown = false;
            int tabsOpenedInCrisis = 0;
            int tabsOpenedPreCrisis = 0;
            bool crisisActActive = false;
            int crisisStartDay = 0;
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
                    case "m_TabsOpenedInCrisis": tabsOpenedInCrisis = KeyedSerializer.ReadInt(reader, tag, "m_TabsOpenedInCrisis"); break;
                    case "m_TabsOpenedPreCrisis": tabsOpenedPreCrisis = KeyedSerializer.ReadInt(reader, tag, "m_TabsOpenedPreCrisis"); break;
                    case "m_CrisisActActive": crisisActActive = KeyedSerializer.ReadBool(reader, tag, "m_CrisisActActive"); break;
                    case "m_CrisisStartDay": crisisStartDay = KeyedSerializer.ReadInt(reader, tag, "m_CrisisStartDay"); break;
                    case "m_FirstWaveEnded": firstWaveEnded = KeyedSerializer.ReadBool(reader, tag, "m_FirstWaveEnded"); break;
                    case "m_FirstWaveCausedDamage": firstWaveCausedDamage = KeyedSerializer.ReadBool(reader, tag, "m_FirstWaveCausedDamage"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new CrisisTutorialPersistState(
                firstStrikeShown,
                exodusWarningShown,
                tabsOpenedInCrisis,
                tabsOpenedPreCrisis,
                crisisActActive,
                crisisStartDay,
                firstWaveEnded,
                firstWaveCausedDamage);
        }
    }
}
