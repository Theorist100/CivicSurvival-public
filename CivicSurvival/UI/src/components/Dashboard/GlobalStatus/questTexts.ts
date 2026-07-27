/**
 * Per-quest text keys, by C# QuestId (Core/Types/QuestTypes.cs).
 *
 * Shared by the GlobalStatus chip and the quest-outcome modals so a quest cannot be
 * named one way on the bar and another in the modal that closes it. A quest missing
 * here renders nothing at all — a save from a newer build must never surface raw
 * localization keys.
 *
 * Adding a quest = one row here plus its localization entries.
 */
export interface QuestTexts {
    chip: string;
    title: string;
    desc: string;
    reward: string;
}

export const QUEST_TEXTS: Record<number, QuestTexts> = {
    1: {
        chip: "UI_QUEST_CHIP_KOTLETA",
        title: "UI_QUEST_KOTLETA_TITLE",
        desc: "UI_QUEST_KOTLETA_DESC",
        reward: "UI_QUEST_KOTLETA_REWARD",
    },
    2: {
        chip: "UI_QUEST_CHIP_VOICE",
        title: "UI_QUEST_VOICE_TITLE",
        desc: "UI_QUEST_VOICE_DESC",
        reward: "UI_QUEST_VOICE_REWARD",
    },
    3: {
        chip: "UI_QUEST_CHIP_AIRDEFENSE",
        title: "UI_QUEST_AIRDEFENSE_TITLE",
        desc: "UI_QUEST_AIRDEFENSE_DESC",
        reward: "UI_QUEST_AIRDEFENSE_REWARD",
    },
};
