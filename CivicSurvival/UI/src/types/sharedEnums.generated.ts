// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/ui-dto.contract.yaml
// SourceHash:       sha256:8c205574c11c79c4dfa169983191fa57ef8a56e50cad289c8c1d10fc1e2f220b
// Generator:        scripts/generators/ui_dto.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

export const Act = {
    PreWar: 0,
    Crisis: 1,
    Exodus: 2,
    Adaptation: 3,
    Routine: 4,
} as const;
export type ActValue = typeof Act[keyof typeof Act];

export const CountermeasuresPhase = {
    Idle: 0,
    Suspicion: 1,
    Investigation: 2,
    WaitingForInvestigationChoice: 3,
    ArticlePublished: 4,
    WaitingForPoliceChoice: 5,
    PoliceInvestigation: 6,
    Arrested: 7,
} as const;
export type CountermeasuresPhaseValue = typeof CountermeasuresPhase[keyof typeof CountermeasuresPhase];

export const DefeatCause = {
    None: 0,
    PopulationCollapse: 1,
    LostControl: 2,
    Arrested: 3,
} as const;
export type DefeatCauseValue = typeof DefeatCause[keyof typeof DefeatCause];

export const FreezeReason = {
    None: 0,
    PoliceInvestigation: 1,
    LowTrustLevel: 2,
    TemporaryPunishment: 4,
    Confiscated: 16,
} as const;
export type FreezeReasonValue = typeof FreezeReason[keyof typeof FreezeReason];

export const FreezeReasonFlags = [
    FreezeReason.PoliceInvestigation,
    FreezeReason.LowTrustLevel,
    FreezeReason.TemporaryPunishment,
    FreezeReason.Confiscated,
] as const;

export const FreezeReasonLocaleKeys: Record<number, string> = {
    [FreezeReason.PoliceInvestigation]: "UI_MARKET_FREEZE_INVESTIGATION",
    [FreezeReason.LowTrustLevel]: "UI_MARKET_FREEZE_LOW_TRUST",
    [FreezeReason.TemporaryPunishment]: "UI_MARKET_FREEZE_PUNISHMENT",
    [FreezeReason.Confiscated]: "UI_MARKET_FREEZE_CONFISCATED",
};

export function decodeFreezeReason(reason: number): readonly number[] {
    return FreezeReasonFlags.filter(flag => (reason & flag) === flag);
}

export const IntroPhase = {
    None: 0,
    Modal: 1,
    Silence: 2,
    Siren: 3,
    Attack: 4,
    Reveal: 5,
    Done: 6,
} as const;
export type IntroPhaseValue = typeof IntroPhase[keyof typeof IntroPhase];

export const ScenarioType = {
    None: 0,
    Village: 1,
    Town: 2,
    City: 3,
} as const;
export type ScenarioTypeValue = typeof ScenarioType[keyof typeof ScenarioType];

export const SocialMoodValues = ["Neutral", "Smug", "Suffering", "Warning", "Angry", "Suspicious", "Paranoid"] as const;
export type SocialMood = typeof SocialMoodValues[number];
export const isSocialMood = (value: unknown): value is SocialMood =>
    typeof value === "string" && (SocialMoodValues as readonly string[]).includes(value);
