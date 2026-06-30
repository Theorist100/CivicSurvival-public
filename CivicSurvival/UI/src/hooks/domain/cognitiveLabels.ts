/**
 * Cognitive enum constants + label-key lookups.
 * Decoupled from useCognitive so the hook stays a thin DTO wrapper.
 */

import { type TranslationKey } from "../../locales";

export const HeroStatus = {
    Inactive: 0,
    Deployed: 1,
    Lecturing: 2,
} as const;

export const ProtestRisk = {
    Low: 0,
    Medium: 1,
    High: 2,
    Critical: 3,
} as const;

export const InternetMode = {
    Open: 0,
    Firewall: 1,
    Blackout: 2,
} as const;

export const NarrativeMode = {
    Soothing: 0,
    Alarmist: 1,
    Realistic: 2,
} as const;

export type HeroStatusType = typeof HeroStatus[keyof typeof HeroStatus];
export type ProtestRiskType = typeof ProtestRisk[keyof typeof ProtestRisk];
export type InternetModeType = typeof InternetMode[keyof typeof InternetMode];
export type NarrativeModeType = typeof NarrativeMode[keyof typeof NarrativeMode];

export function getHeroStatusLabelKey(status: number): TranslationKey {
    switch (status) {
        case HeroStatus.Deployed: return "UI_HERO_STATUS_DEPLOYED";
        case HeroStatus.Lecturing: return "UI_HERO_STATUS_LECTURING";
        default: return "UI_HERO_STATUS_INACTIVE";
    }
}

export function getProtestRiskLabelKey(risk: number): TranslationKey {
    switch (risk) {
        case ProtestRisk.Critical: return "UI_PROTEST_RISK_CRITICAL";
        case ProtestRisk.High: return "UI_PROTEST_RISK_HIGH";
        case ProtestRisk.Medium: return "UI_PROTEST_RISK_MEDIUM";
        default: return "UI_PROTEST_RISK_LOW";
    }
}
