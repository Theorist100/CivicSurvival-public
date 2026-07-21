/**
 * Localization bindings - current locale from C#
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { type ModLanguageId } from "../../types/semantic";
import { B } from "../bindingNames.generated";

// Language enum values (mirrors C# ModLanguage)
export const ModLanguage = {
    GameDefault: 0,
    English: 1,
    Ukrainian: 2,
    German: 3,
    Spanish: 4,
    French: 5,
    Polish: 6,
    Chinese: 7
} as const;

export type ModLanguageType = typeof ModLanguage[keyof typeof ModLanguage];

// Set language preference
export const setLanguage = (language: ModLanguageId) => {
    triggerCivic(B.SetLanguage, language);
};
