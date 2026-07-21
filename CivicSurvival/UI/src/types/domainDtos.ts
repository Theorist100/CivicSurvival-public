/**
 * Domain DTO types and validators.
 *
 * Interfaces, validators AND the DEFAULT_* default objects are generated from
 * Docs/Contracts/ui-dto.contract.yaml (see emitDefault: true / default: on
 * individual fields there). Run: python ../../scripts/generate.py ui-dto
 *
 * What's left hand-written here: historical type aliases, the isThreatDto
 * MapBounds-aware override, the two UI-only sentinel constants, and the
 * debug-toggle snapshot (no contract entity — internal dev tooling only).
 *
 * SUB-TYPES for Raw JSON fields are in dtoSubTypes.ts.
 */

// Imports first (eslint import/first)
import { isRecord } from "../utils/typeGuards";
import {
    isMapBoundsDto,
    isThreatDto as isGeneratedThreatDto,
    type AttackTimeEstimateDto,
    type FocusRangeDto,
    type OfficialTreasuryDto,
    type ShadowWalletDto,
    type ThreatDto as GeneratedThreatDto,
} from './domainDtos.generated';

// Re-export under historical names so existing consumers don't churn.
export type AttackTimeEstimate = AttackTimeEstimateDto;
export type FocusRange = FocusRangeDto;
export type OfficialTreasury = OfficialTreasuryDto;
export type ShadowWallet = ShadowWalletDto;

// Re-export generated interfaces (source of truth: ui-dto contract)
export type {
    AirDefenseDto,
    ArrestedModalPayloadDto,
    AttentionDto,
    BackupPowerDto,
    BuckwheatDto,
    CognitiveDto,
    CountermeasuresDto,
    CrisisSweepDto,
    DonorDto,
    ExportDto,
    FinanceDto,
    GridWarfareDto,
    ImportDto,
    IntelDto,
    MaintenanceDto,
    MobilizationDto,
    NewsDto,
    PowerGridDto,
    ReputationDto,
    SchemesDto,
    SettingsDto,
    SettingsLocalizationDto,
    SpotterDto,
    ThreatDto,
} from './domainDtos.generated';

export {
    isActionAvailability,
    isAirDefenseDto,
    isArrestedModalPayloadDto,
    isAttentionDto,
    isBackupPowerDto,
    isBuckwheatDto,
    isCognitiveDto,
    isCountermeasuresDto,
    isDonorDto,
    isExportDto,
    isFinanceDto,
    isGridWarfareDto,
    isImportDto,
    isIntelDto,
    isMaintenanceDto,
    isMobilizationDto,
    isNewsDto,
    isPowerGridDto,
    isReputationDto,
    isSchemesDto,
    isSettingsDto,
    isSettingsLocalizationDto,
    isSpotterDto,
} from './domainDtos.generated';

// Re-export hand-written sub-types (Raw JSON field shapes)
export * from './dtoSubTypes';

export function isThreatDto(value: unknown): value is GeneratedThreatDto {
    return isGeneratedThreatDto(value) && isMapBoundsDto(value.MapBounds);
}

// Re-export generated defaults (source of truth: ui-dto contract emitDefault:
// true entries + per-field default: overrides).
export {
    DEFAULT_ACTION_AVAILABILITY,
    DEFAULT_AIR_DEFENSE_DTO,
    DEFAULT_ATTACK_TIME_ESTIMATE,
    DEFAULT_ATTENTION_DTO,
    DEFAULT_BACKUP_POWER_DTO,
    DEFAULT_BUCKWHEAT_DTO,
    DEFAULT_COGNITIVE_DTO,
    DEFAULT_COUNTERMEASURES_DTO,
    DEFAULT_CRISIS_SWEEP_DTO,
    DEFAULT_DONOR_DTO,
    DEFAULT_EXPORT_DTO,
    DEFAULT_FINANCE_DTO,
    DEFAULT_FOCUS_RANGE,
    DEFAULT_GRID_WARFARE_DTO,
    DEFAULT_IMPORT_DTO,
    DEFAULT_INTEL_DTO,
    DEFAULT_MAINTENANCE_DTO,
    DEFAULT_MAP_BOUNDS,
    DEFAULT_MOBILIZATION_DTO,
    DEFAULT_NEWS_DTO,
    DEFAULT_OFFICIAL_TREASURY,
    DEFAULT_POWER_GRID_DTO,
    DEFAULT_REPUTATION_DTO,
    DEFAULT_SCHEMES_DTO,
    DEFAULT_SHADOW_WALLET,
    DEFAULT_SPOTTER_DTO,
    DEFAULT_THREAT_DTO,
} from './domainDtos.generated';

/**
 * Placeholder reason carried by DEFAULT_ACTION_AVAILABILITY while the
 * publishing system is feature-gated to a later beta wave (WaveLocked).
 * Tooltips translate it; inline status lines should suppress it — the
 * GlassCase WAVE badge already communicates the lock on those panels.
 */
export const WAVE_LOCKED_REASON_ID = "UI_ACTION_WAVE_LOCKED";

/**
 * "No camera position" sentinel for ThreatDto.CameraX/CameraZ. Mirrors
 * ThreatDto.CameraMarkerSentinel (C#). After map normalization it lands far
 * outside [0,100], so the radar omits the "you are here" marker.
 */
export const CAMERA_MARKER_SENTINEL = -1_000_000_000;

export type DebugToggleGroup = "threat" | "domain" | "core" | "sub";

export interface DebugToggleEntry {
    key: string;
    enabled: boolean;
    canDisable: boolean;
    lockedReasonId: string;
    group: DebugToggleGroup;
    parent: string;
    systemCount: number;
}

export interface DebugToggleSnapshot {
    version: number;
    entries: DebugToggleEntry[];
}

export const DEFAULT_DEBUG_TOGGLE_SNAPSHOT: DebugToggleSnapshot = {
    version: 0,
    entries: [],
};

const isDebugToggleGroup = (value: unknown): value is DebugToggleGroup =>
    value === "threat" || value === "domain" || value === "core" || value === "sub";

export function isDebugToggleEntry(value: unknown): value is DebugToggleEntry {
    if (!isRecord(value)) return false;
    const record = value;
    return typeof record.key === "string"
        && typeof record.enabled === "boolean"
        && typeof record.canDisable === "boolean"
        && typeof record.lockedReasonId === "string"
        && isDebugToggleGroup(record.group)
        && typeof record.parent === "string"
        && typeof record.systemCount === "number";
}

export function isDebugToggleSnapshot(value: unknown): value is DebugToggleSnapshot {
    if (!isRecord(value)) return false;
    const record = value;
    return typeof record.version === "number"
        && Array.isArray(record.entries)
        && record.entries.every(isDebugToggleEntry);
}
