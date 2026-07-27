using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted state of the narrative quests (QuestSystem): a slot list, one keyed
    /// block per quest. Slots for quest ids unknown to the running build are skipped
    /// on read (forward-compatible); slots absent from the save keep boot defaults
    /// (backward-compatible). Deadlines/cooldowns are absolute TotalGameHours.
    /// </summary>
    public static class QuestStateCodec
    {
        // Backstop for a corrupted count — far above any realistic quest roster.
        private const int MAX_SLOTS = 64;

        public static void Write<TWriter>(System.ReadOnlySpan<QuestSlot> slots, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteBufferHeader(writer, "quests", slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                ref readonly var slot = ref slots[i];
                KeyedSerializer.WriteBlockHeader(writer, 9);
                KeyedSerializer.WriteField(writer, "id", (int)slot.Id);
                KeyedSerializer.WriteField(writer, "ph", (int)slot.Phase);
                KeyedSerializer.WriteField(writer, "off", slot.Offered);
                KeyedSerializer.WriteField(writer, "prg", slot.Progress);
                KeyedSerializer.WriteField(writer, "tgt", slot.Target);
                KeyedSerializer.WriteField(writer, "anc", slot.Anchor);
                KeyedSerializer.WriteField(writer, "dl", slot.DeadlineHour);
                KeyedSerializer.WriteField(writer, "cd", slot.CooldownUntilHour);
                KeyedSerializer.WriteField(writer, "clr", slot.ClearAtHour);
            }
        }

        /// <summary>
        /// Read the slot list into <paramref name="apply"/> — called once per decoded slot;
        /// the consumer matches ids to its live roster and ignores unknown ones.
        /// </summary>
        public static void Read<TReader>(TReader reader, System.Action<QuestSlot> apply)
            where TReader : IReader
        {
            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                if (key != "quests")
                {
                    KeyedSerializer.Skip(reader, tag);
                    continue;
                }

                int count = KeyedSerializer.ReadBufferCount(reader, tag, "quests", MAX_SLOTS);
                for (int s = 0; s < count; s++)
                    apply(ReadSlot(reader));
            }
        }

        private static QuestSlot ReadSlot<TReader>(TReader reader)
            where TReader : IReader
        {
            var slot = new QuestSlot { Anchor = QuestSlot.NO_ANCHOR };
            int id = 0;
            int phase = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int f = 0; f < fieldCount; f++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "id":
                        id = KeyedSerializer.ReadInt(reader, tag, "id");
                        break;
                    case "ph":
                        phase = KeyedSerializer.ReadInt(reader, tag, "ph");
                        break;
                    case "off":
                        slot.Offered = KeyedSerializer.ReadBool(reader, tag, "off");
                        break;
                    case "prg":
                        slot.Progress = KeyedSerializer.ReadInt(reader, tag, "prg");
                        break;
                    case "tgt":
                        slot.Target = KeyedSerializer.ReadInt(reader, tag, "tgt");
                        break;
                    case "anc":
                        slot.Anchor = KeyedSerializer.ReadInt(reader, tag, "anc");
                        break;
                    case "dl":
                        slot.DeadlineHour = KeyedSerializer.ReadDouble(reader, tag, "dl");
                        break;
                    case "cd":
                        slot.CooldownUntilHour = KeyedSerializer.ReadDouble(reader, tag, "cd");
                        break;
                    case "clr":
                        slot.ClearAtHour = KeyedSerializer.ReadDouble(reader, tag, "clr");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            // Defined-checked decodes (CIVIC140): corrupted bytes degrade to None/Inactive.
            // A save from a newer build carrying a quest id this build does not define also
            // decodes to None — the consumer's roster match drops it either way, so the
            // forward-compat behavior (unknown quests are skipped) is preserved.
            slot.Id = id > 0 && id <= byte.MaxValue && System.Enum.IsDefined(typeof(QuestId), (byte)id)
                ? (QuestId)id : QuestId.None;
            slot.Phase = phase >= 0 && phase <= byte.MaxValue && System.Enum.IsDefined(typeof(QuestPhase), (byte)phase)
                ? (QuestPhase)phase : QuestPhase.Inactive;
            return slot;
        }
    }
}
