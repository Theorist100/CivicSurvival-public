// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/modal.contract.yaml
// SourceHash:       sha256:21a652933c91616d9af83aaa85e25ac0f55f6f7f3524a24ddad44ce6b7e52863
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
    "ModUpdatedRestart",
    "Victory",
    "WarFatigue",
    "OnlineConsent",
    "Intro",
    "FirstStrike",
    "GridCollapse",
    "ExodusWarning",
    "PsyRaidInbound",
    "ModUpdatePending",
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
    "PsyDebriefing",
] as const;

export type ModalId = typeof MODAL_IDS[number];
