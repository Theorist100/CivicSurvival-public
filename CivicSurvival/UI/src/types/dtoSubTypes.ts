/**
 * Hand-written sub-types for pre-serialized JSON fields (C# w.Raw() calls).
 * These shapes can't be extracted from C# DTO structs because serialization
 * happens in domain systems, not in the DTO itself.
 *
 * Used by domainDtos.generated.ts (imported) and domainDtos.ts (re-exported).
 */

import {
    type BackupPolicy,
    type DifficultyPresetId,
    type ModLanguageId,
    type ProcurementLevel,
    type SchedulePresetId,
    type UIThemeId,
} from "./semantic";
import { isRecord } from "../utils/typeGuards";

export type { EntityRef } from "./entityRef";

// ── Threat Radar ────────────────────────────────────────────────────────────

export type GridStatus = "normal" | "warning" | "critical" | "surplus" | "unknown";
export type GridStressZone = "normal" | "yellow" | "red" | "collapsed";
export type WavePhase = "calm" | "alert" | "attack" | "recovery";
export type WaveDataStatus = "unavailable" | "noWave" | "preStart" | "completed" | "active";
export type ShockTier = "DeepConcern" | "Headlines" | "GlobalShock";
export type TensionStatus = "LOW" | "ELEVATED" | "HIGH" | "CRITICAL";
export type GlobalConnectionStatus = "Disconnected" | "Disconnected by user" | "Connected to Global Grid" | string;
export type BackupPolicyId = BackupPolicy;
export type BribeRiskWarning = "" | "RISK_BRIBE_INVESTIGATION_WARNING" | "RISK_BRIBE_POLICE_WARNING";
export type CounterChoiceType = 0 | 1 | 2;
export type CounterHeatLevel =
    | "Safe"
    | "Warning"
    | "Danger"
    | "Critical"
    | "UI_COUNTER_HEAT_LEVEL_SAFE"
    | "UI_COUNTER_HEAT_LEVEL_WARNING"
    | "UI_COUNTER_HEAT_LEVEL_DANGER"
    | "UI_COUNTER_HEAT_LEVEL_CRITICAL";
export type CounterPhase =
    | "Clear"
    | "Idle"
    | "Suspicion"
    | "Investigation"
    | "Waiting for Decision"
    | "Article Published"
    | "Police Investigation"
    | "Under Investigation"
    | "Arrested"
    | "Unknown"
    | "UI_COUNTER_PHASE_IDLE"
    | "UI_COUNTER_PHASE_SUSPICION"
    | "UI_COUNTER_PHASE_INVESTIGATION"
    | "UI_COUNTER_PHASE_WAITING_DECISION"
    | "UI_COUNTER_PHASE_ARTICLE_PUBLISHED"
    | "UI_COUNTER_PHASE_POLICE_DECISION"
    | "UI_COUNTER_PHASE_UNDER_INVESTIGATION"
    | "UI_COUNTER_PHASE_ARRESTED"
    | "UI_COUNTER_PHASE_UNKNOWN";
export type DefensePolicyId = 0 | 1;
export type FreezeReason = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19 | 20 | 21 | 22 | 23 | 24 | 25 | 26 | 27 | 28 | 29 | 30 | 31;
export type PowerScheduleId = SchedulePresetId;
export type ProcurementLevelId = ProcurementLevel;
export type SettingsDifficultyPreset = DifficultyPresetId;
export type SettingsLanguagePreference = ModLanguageId;
export type SettingsTheme = UIThemeId;

export type EvasionStatus = 'targeted' | 'evasive' | 'hardlock';
export type ThreatType = 'shahed' | 'ballistic';

// ThreatTarget, RadarThreat, RadarTarget, RadarInterception and MapBounds are
// now generated from ui-dto.contract.yaml as the *Dto subtypes; consumers
// import them from ./domainDtos.generated with PascalCase wire fields.

// OfficialTreasury and ShadowWallet are now generated from
// ui-dto.contract.yaml as the *Dto subtypes; consumers import them from
// ./domainDtos.generated with PascalCase wire fields.


// ── Air Defense ─────────────────────────────────────────────────────────────

export interface FocusRange {
    min: number;
    max: number;
}

export type AttackTimeEstimate =
    | { status: "unknown" }
    | { status: "in-attack" }
    | { status: "in-recovery" }
    | { status: "awaiting-window" }
    | { status: "available"; minHours?: number; maxHours?: number };

export const isWavePhase = (value: unknown): value is WavePhase =>
    value === "calm" || value === "alert" || value === "attack" || value === "recovery";

export const isWaveDataStatus = (value: unknown): value is WaveDataStatus =>
    value === "unavailable" || value === "noWave" || value === "preStart" || value === "completed" || value === "active";

export interface RequestResult {
    RequestId: number;
    Status: "idle" | "pending" | "success" | "failed";
    ReasonId: string;
    CanonicalEcho: string;
    DiscriminatorKind: "none" | "districtIndex" | "offerKey" | "field" | "operationSlot";
    DiscriminatorValue: string;
}

export const isRequestResult = (value: unknown): value is RequestResult =>
    isRecord(value)
    && typeof value.RequestId === "number"
    && (
        value.Status === "idle"
        || value.Status === "pending"
        || value.Status === "success"
        || value.Status === "failed"
    )
    && typeof value.ReasonId === "string"
    && typeof value.CanonicalEcho === "string"
    && (
        value.DiscriminatorKind === "none"
        || value.DiscriminatorKind === "districtIndex"
        || value.DiscriminatorKind === "offerKey"
        || value.DiscriminatorKind === "field"
        || value.DiscriminatorKind === "operationSlot"
    )
    && typeof value.DiscriminatorValue === "string";

export type GridOperationType = "drone" | "blackout" | "disinfo";
export type AttackCosts = Record<GridOperationType, number>;
export type EnemyStance = "IronDome" | "FaradayCage" | "MediaShield";
export type StancePhase = "Active" | "Vulnerable";
export type NextEnemyStance = EnemyStance | "";

// ── Corruption / Procurement ───────────────────────────────────────────────

// PendingProcurementOfferEntry and ActiveContractEntry are now generated from
// ui-dto.contract.yaml as subtypes; consumers import them from
// ./domainDtos.generated. The hand-written PendingOffer that lived here
// drifted from the C# producer (9 fields vs 15) and silently masked the
// real wire shape — kept the lesson, removed the duplicate.

// ShadowProgramEntry is now generated from ui-dto.contract.yaml as a subtype;
// consumers import it from ./domainDtos.generated with PascalCase wire fields.

// CivilianDamageData is now generated from ui-dto.contract.yaml as a subtype;
// it embeds an EntityRefDto (PascalCase wire shape) instead of the camelCase
// EntityRef used by Coherent UI's trigger ValueReaders.
