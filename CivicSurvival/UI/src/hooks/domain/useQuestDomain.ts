/**
 * Narrative quest hook — state for the GlobalStatus quest chip.
 */

import { questState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_QUEST_DTO, isQuestDto } from "../../types/domainDtos";

export const useQuests = () =>
    useDtoBinding(questState$, isQuestDto, { debugName: "questState", defaultValue: DEFAULT_QUEST_DTO });
