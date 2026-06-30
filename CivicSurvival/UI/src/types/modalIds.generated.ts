// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/modal.contract.yaml
// SourceHash:       sha256:9f6e8df1023e805d39aa3e20ab44f550738f77792cf1de7c75bee8c638aaeb71
// Generator:        scripts/generators/modal.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-06-06T00:00:00Z

// Single source of truth for modal ids: Docs/Contracts/modal.contract.yaml.
// ModalId, the runtime id set, and the modal registry all derive from this array.
export const MODAL_IDS = [
    "Arrested",
    "Defeat",
    "ModLoadFailure",
    "Victory",
    "WarFatigue",
    "OnlineConsent",
    "Intro",
    "FirstStrike",
    "GridCollapse",
    "ExodusWarning",
    "GridCritical",
    "WarBegins",
    "FirstDonorAid",
    "FirstSuccessfulDefense",
    "GeneratorEra",
    "SpotterAlert",
    "CorruptionOffer",
    "GhostTown",
    "WhoStaysBehind",
    "Refugee",
    "Collapse",
    "Debriefing",
] as const;

export type ModalId = typeof MODAL_IDS[number];
