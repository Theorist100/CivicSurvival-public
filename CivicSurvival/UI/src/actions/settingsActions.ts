/**
 * @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks.
 *
 * Settings domain actions — telemetry, error reporting.
 * FIX S10-03: These triggers exist in C# SettingsUISystem but were missing from TS.
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";

// ============ Telemetry ============

/**
 * Tell C# whether the crash-dump list is on screen.
 *
 * The list is a directory stat rebuilt on the 500ms settings tick, so it must not run while
 * nobody is looking at it. This replaces the old `TogglePanel`, which C# used for the same
 * gate by tracking whether the settings WINDOW was open — the last piece of mod UI state
 * that lived outside React, and the reason the window could reappear over the main menu.
 * Settings are dashboard views now; only this one narrow fact still needs to reach C#.
 */
export const setCrashDumpScan = (scanning: boolean): void =>
    triggerCivic(B.SetCrashDumpScan, scanning);

export const setDifficultyPreset = (presetId: number): void =>
    triggerCivic(B.SetDifficultyPreset, presetId);

export const setConstructionDelay = (enabled: boolean): void =>
    triggerCivic(B.SetConstructionDelay, enabled);

export const setRandomDisasters = (enabled: boolean): void =>
    triggerCivic(B.SetRandomDisasters, enabled);

export const setWinterMultiplier = (enabled: boolean): void =>
    triggerCivic(B.SetWinterMultiplier, enabled);

export const setNeighborEnvy = (enabled: boolean): void =>
    triggerCivic(B.SetNeighborEnvy, enabled);

export const setBackupPower = (enabled: boolean): void =>
    triggerCivic(B.SetBackupPower, enabled);

export const setProtectCriticalInfra = (enabled: boolean): void =>
    triggerCivic(B.SetProtectCriticalInfra, enabled);

export const setTelemetryEnabled = (enabled: boolean): void =>
    triggerCivic(B.SetTelemetryEnabled, enabled);

export const setUITheme = (themeId: number): void =>
    triggerCivic(B.SetUITheme, themeId);

// ============ Mod Audio ============

export const setMuteCivicAudio = (enabled: boolean): void =>
    triggerCivic(B.SetMuteCivicAudio, enabled);

export const setMuteDroneAudio = (enabled: boolean): void =>
    triggerCivic(B.SetMuteDroneAudio, enabled);

export const setMuteAlertAudio = (enabled: boolean): void =>
    triggerCivic(B.SetMuteAlertAudio, enabled);

export const setMuteCombatAudio = (enabled: boolean): void =>
    triggerCivic(B.SetMuteCombatAudio, enabled);

// ============ Error Reporting ============

export const sendReport = (): void =>
    triggerCivic(B.SendReport);

/** Load-failure report (ModLoadFailure modal): standard report + Modding.log head + PdxSdk.log sync/removal evidence. */
export const sendLoadFailureReport = (): void =>
    triggerCivic(B.SendLoadFailureReport);

export const copyReport = (): void =>
    triggerCivic(B.CopyReport);

export const sendModLog = (): void =>
    triggerCivic(B.SendModLog);

/** Send the selected native crash dumps. `names` is a comma-separated list of dump file names. */
export const sendCrashDumps = (names: string): void =>
    triggerCivic(B.SendCrashDumps, names);

export const clearErrors = (): void =>
    triggerCivic(B.ClearErrors);

// ============ External Links ============

/** Link ids whitelisted on the C# side (SettingsUISystem) — never raw URLs. */
const EXTERNAL_LINK_DISCORD = 0;
const EXTERNAL_LINK_PRIVACY = 1;

// Single trigger call site for the whitelisted external-link binding; each public
// action just supplies its whitelisted id (the C# side maps id → URL).
const openExternalLink = (linkId: number): void =>
    triggerCivic(B.OpenExternalLink, linkId);

export const openDiscord = (): void => openExternalLink(EXTERNAL_LINK_DISCORD);

export const openPrivacy = (): void => openExternalLink(EXTERNAL_LINK_PRIVACY);
