using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ScenarioStateMachinePersistState
    {
        public ScenarioStateMachinePersistState(
            string modVersion,
            ScenarioState state,
            int postCrisisActStartDay,
            bool milestoneWeekShown,
            bool milestoneMonthShown,
            bool milestoneQuarterShown)
        {
            ModVersion = modVersion ?? string.Empty;
            State = state;
            PostCrisisActStartDay = postCrisisActStartDay;
            MilestoneWeekShown = milestoneWeekShown;
            MilestoneMonthShown = milestoneMonthShown;
            MilestoneQuarterShown = milestoneQuarterShown;
        }

        public string ModVersion { get; }
        public ScenarioState State { get; }
        public int PostCrisisActStartDay { get; }
        public bool MilestoneWeekShown { get; }
        public bool MilestoneMonthShown { get; }
        public bool MilestoneQuarterShown { get; }
    }

    public static class ScenarioStateMachineCodec
    {
        public static void Write<TWriter>(in ScenarioStateMachinePersistState snapshot, TWriter writer)
            where TWriter : IWriter
        {
            var state = snapshot.State;
            KeyedSerializer.WriteBlockHeader(writer, 26);
            KeyedSerializer.WriteField(writer, "modVersion", snapshot.ModVersion);
            KeyedSerializer.WriteEnumIntField(writer, "type", (int)state.Type);
            KeyedSerializer.WriteEnumIntField(writer, "currentAct", (int)state.CurrentAct);
            KeyedSerializer.WriteField(writer, "warDay", state.WarDay);
            KeyedSerializer.WriteField(writer, "warStartTime", state.WarStartTime);
            KeyedSerializer.WriteField(writer, "originalPopulation", state.OriginalPopulation);
            KeyedSerializer.WriteField(writer, "peakPopulation", state.PeakPopulation);
            KeyedSerializer.WriteField(writer, "refugeesReceived", state.RefugeesReceived);
            KeyedSerializer.WriteField(writer, "citizensLeft", state.CitizensLeft);
            KeyedSerializer.WriteEnumIntField(writer, "shownModals", (int)state.ShownModals);
            KeyedSerializer.WriteField(writer, "actProgress", state.ActProgress);
            KeyedSerializer.WriteField(writer, "wavesDefended", state.WavesDefended);
            KeyedSerializer.WriteField(writer, "donorAidReceived", state.DonorAidReceived);
            KeyedSerializer.WriteField(writer, "exodusRateOverrideFraction", state.ExodusRateOverrideFraction);
            KeyedSerializer.WriteField(writer, "skipIntro", state.SkipIntro);
            KeyedSerializer.WriteField(writer, "missilesIntercepted", state.MissilesIntercepted);
            KeyedSerializer.WriteField(writer, "blackoutRecoveries", state.BlackoutRecoveries);
            KeyedSerializer.WriteField(writer, "buildingsDamaged", state.BuildingsDamaged);
            KeyedSerializer.WriteField(writer, "isDefeated", state.IsDefeated);
            KeyedSerializer.WriteEnumIntField(writer, "defeatCause", (int)state.DefeatCause);
            KeyedSerializer.WriteField(writer, "defeatDismissed", state.DefeatDismissed);
            KeyedSerializer.WriteEnumIntField(writer, "postVictoryMode", (int)state.PostVictoryMode);
            KeyedSerializer.WriteField(writer, "postCrisisActStartDay", snapshot.PostCrisisActStartDay);
            KeyedSerializer.WriteField(writer, "milestoneWeekShown", snapshot.MilestoneWeekShown);
            KeyedSerializer.WriteField(writer, "milestoneMonthShown", snapshot.MilestoneMonthShown);
            KeyedSerializer.WriteField(writer, "milestoneQuarterShown", snapshot.MilestoneQuarterShown);
        }

        public static void Read<TReader>(TReader reader, out ScenarioStateMachinePersistState snapshot)
            where TReader : IReader
        {
            string modVersion = string.Empty;
            var state = ScenarioState.CreateDefault();
            int postCrisisActStartDay = 0;
            bool milestoneWeekShown = false;
            bool milestoneMonthShown = false;
            bool milestoneQuarterShown = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "modVersion": modVersion = KeyedSerializer.ReadString(reader, tag, "modVersion", "pre-0.1.0"); break;
                    case "type": state.Type = KeyedSerializer.ReadEnumInt<TReader, ScenarioType>(reader, tag, "type", ScenarioType.None); break;
                    case "currentAct": state.CurrentAct = KeyedSerializer.ReadEnumInt<TReader, Act>(reader, tag, "currentAct", Act.PreWar); break;
                    case "warDay": state.WarDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "warDay", 0, 100000); break;
                    case "warStartTime": state.WarStartTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "warStartTime", 0f); break;
                    case "originalPopulation": state.OriginalPopulation = KeyedSerializer.ReadBoundedInt(reader, tag, "originalPopulation", 0, 10_000_000, 0); break;
                    case "peakPopulation": state.PeakPopulation = KeyedSerializer.ReadBoundedInt(reader, tag, "peakPopulation", 0, 10_000_000, 0); break;
                    case "refugeesReceived": state.RefugeesReceived = KeyedSerializer.ReadBoundedInt(reader, tag, "refugeesReceived", 0, 10_000_000, 0); break;
                    case "citizensLeft": state.CitizensLeft = KeyedSerializer.ReadBoundedInt(reader, tag, "citizensLeft", 0, 10_000_000, 0); break;
                    case "shownModals":
                        state.ShownModals = ReadModalFlags(reader, tag);
                        break;
                    case "actProgress": state.ActProgress = KeyedSerializer.ReadSafeFloat(reader, tag, "actProgress", 0f, 1f, 0f); break;
                    case "wavesDefended": state.WavesDefended = KeyedSerializer.ReadMonotonicCounter(reader, tag, "wavesDefended", 0, 100000); break;
                    case "donorAidReceived": state.DonorAidReceived = KeyedSerializer.ReadMonotonicCounter(reader, tag, "donorAidReceived", 0, 100000); break;
                    case "exodusRateOverrideFraction": state.ExodusRateOverrideFraction = KeyedSerializer.ReadSafeFloat(reader, tag, "exodusRateOverrideFraction", 0f, 1f, 0f); break;
                    case "skipIntro": state.SkipIntro = KeyedSerializer.ReadBool(reader, tag, "skipIntro"); break;
                    case "missilesIntercepted": state.MissilesIntercepted = KeyedSerializer.ReadMonotonicCounter(reader, tag, "missilesIntercepted", 0, 10_000_000); break;
                    case "blackoutRecoveries": state.BlackoutRecoveries = KeyedSerializer.ReadMonotonicCounter(reader, tag, "blackoutRecoveries", 0, 100000); break;
                    case "buildingsDamaged": state.BuildingsDamaged = KeyedSerializer.ReadMonotonicCounter(reader, tag, "buildingsDamaged", 0, 10_000_000); break;
                    case "isDefeated": state.IsDefeated = KeyedSerializer.ReadBool(reader, tag, "isDefeated"); break;
                    case "defeatCause": state.DefeatCause = KeyedSerializer.ReadEnumInt<TReader, DefeatCause>(reader, tag, "defeatCause", DefeatCause.None); break;
                    case "defeatDismissed": state.DefeatDismissed = KeyedSerializer.ReadBool(reader, tag, "defeatDismissed"); break;
                    case "postVictoryMode": state.PostVictoryMode = KeyedSerializer.ReadEnumInt<TReader, PostVictoryMode>(reader, tag, "postVictoryMode", PostVictoryMode.None); break;
                    case "postCrisisActStartDay": postCrisisActStartDay = KeyedSerializer.ReadBoundedInt(reader, tag, "postCrisisActStartDay", 0, 100000, 0); break;
                    case "milestoneWeekShown": milestoneWeekShown = KeyedSerializer.ReadBool(reader, tag, "milestoneWeekShown"); break;
                    case "milestoneMonthShown": milestoneMonthShown = KeyedSerializer.ReadBool(reader, tag, "milestoneMonthShown"); break;
                    case "milestoneQuarterShown": milestoneQuarterShown = KeyedSerializer.ReadBool(reader, tag, "milestoneQuarterShown"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            snapshot = new ScenarioStateMachinePersistState(
                modVersion,
                state,
                postCrisisActStartDay,
                milestoneWeekShown,
                milestoneMonthShown,
                milestoneQuarterShown);
        }

        private static ModalFlags ReadModalFlags<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumInt, "shownModals"))
            {
                return ModalFlags.None;
            }

            reader.Read(out int raw);
#pragma warning disable CIVIC140 // ModalFlags is a [Flags] bitmask; unknown bits are stripped before the cast.
            return (ModalFlags)((uint)raw & (uint)ModalFlags.AllFlags);
#pragma warning restore CIVIC140
        }
    }
}
