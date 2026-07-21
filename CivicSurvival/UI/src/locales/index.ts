/**
 * Unified Localization module for Civic Survival UI
 *
 * Single source of truth: CivicSurvival/Localization/*.json
 * - C# loads JSON as embedded resource
 * - C# sends all strings to UI via binding
 * - TypeScript uses binding for runtime values
 * - TypeScript imports JSON for type extraction (compile time)
 *
 * Usage:
 *   import { useLocale, TranslationKey } from "../locales";
 *   const l = useLocale();
 *
 *   // Simple text
 *   <div>{l.t("UI_GRID_STATUS")}</div>
 *
 *   // Text with interpolation
 *   <div>{l.t("UI_SANCTIONED_DAYS", 5)}</div>  // "SANCTIONED - 5 DAYS"
 *
 *   // Tooltip (returns null if KEY_TIP doesn't exist)
 *   <Tooltip tooltip={l.tip("UI_GRID_STATUS")}>...</Tooltip>
 *
 *   // Tooltip with interpolation
 *   <span title={l.tip("UI_BRIBE_COST", 50)}>...</span>  // "Bribe cost: $50k"
 */

import { useEffect, useMemo, useCallback, useRef } from "react";
import { scWarn } from "../utils/logging";
import { settingsLocalizationState$ } from "../hooks/bindings/domainJsonBindings";
import { useDtoBinding } from "../hooks/domain/useDtoBinding";
import { isSettingsLocalizationDto } from "../types/domainDtos";

// Import JSON for type extraction only (compile time)

import type enUS from "localization/en-US.json";

// Type from JSON keys
export type TranslationKey = keyof typeof enUS;

// Interpolation argument types
type InterpolationArg = string | number;
type MissingKeyKind = "static" | "dynamic";

/**
 * Interpolate {0}, {1}, etc. placeholders with provided arguments.
 * Matches C# string.Format() behavior.
 */
function interpolate(template: string, args: InterpolationArg[]): string {
    if (args.length === 0) return template;
    return template.replace(/\{(\d+)\}/g, (match, index) => {
        const i = Number.parseInt(index, 10);
        return i < args.length ? String(args[i]) : match;
    });
}

/**
 * Main localization hook with interpolation support.
 *
 * @returns Object with t() and tip() functions
 *
 * @example
 * const l = useLocale();
 * l.t("UI_KEY")              // Simple translation
 * l.t("UI_KEY", 5, "text")   // With interpolation: "Value is {0} and {1}"
 * l.tip("UI_KEY")            // Tooltip (looks for UI_KEY_TIP)
 * l.tip("UI_KEY", 10)        // Tooltip with interpolation
 */
export function useLocale() {
    const localizationState = useDtoBinding(
        settingsLocalizationState$,
        isSettingsLocalizationDto,
        { debugName: "settingsLocalizationState" }
    );

    // Extract localization data + version from dedicated localization JSON.
    // Also updates the non-hook translation cache on every parse.
    const { extracted, version } = useMemo(() => {
        if (localizationState.status !== "ready") {
            return { extracted: {} as Record<string, string>, version: 0 };
        }
        return {
            extracted: localizationState.data.LocalizationStrings,
            version: localizationState.data.LocaleVersion,
        };
    }, [localizationState]);

    // Only update strings reference when locale version actually changes
    // (not on every settings update like theme/difficulty changes).
    // `version` is a change token — extracted has same locale data if version is unchanged.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    const strings = useMemo(() => extracted, [version]);
    const missingKeyQueueRef = useRef<Array<{ kind: MissingKeyKind; key: string }>>([]);
    const reportedMissingKeysRef = useRef<Set<string>>(new Set());

    const queueMissingKey = useCallback(
        (kind: MissingKeyKind, key: string): void => {
            if (localizationState.status !== "ready") return;

            const marker = `${kind}:${key}`;
            if (reportedMissingKeysRef.current.has(marker)) return;

            reportedMissingKeysRef.current.add(marker);
            missingKeyQueueRef.current.push({ kind, key });
        },
        [localizationState.status]
    );

    useEffect(() => {
        const queued = missingKeyQueueRef.current;
        if (queued.length === 0) return;

        missingKeyQueueRef.current = [];
        for (const item of queued) {
            scWarn(item.kind === "dynamic"
                ? `[L10n] Missing key (dynamic): ${item.key}`
                : `[L10n] Missing key: ${item.key}`);
        }
    });

    /**
     * Translate a key with optional interpolation arguments.
     * @param key - Translation key from JSON
     * @param args - Values to replace {0}, {1}, etc.
     * @returns Translated string or [KEY] if missing
     */
    const t = useCallback(
        (key: TranslationKey, ...args: InterpolationArg[]): string => {
            const value = strings[key];
            if (!value) {
                queueMissingKey("static", key);
                return `[${key}]`;
            }
            return interpolate(value, args);
        },
        [queueMissingKey, strings]
    );

    /**
     * Get tooltip for a key (looks for KEY_TIP).
     * @param key - Base key (will look for KEY_TIP)
     * @param args - Values to replace {0}, {1}, etc.
     * @returns Tooltip string or null if KEY_TIP doesn't exist
     */
    const tip = useCallback(
        (key: TranslationKey, ...args: InterpolationArg[]): string | null => {
            const tipKey = `${key}_TIP`;
            const value = strings[tipKey];
            if (!value) return null;
            return interpolate(value, args);
        },
        [strings]
    );

    /**
     * Translate a dynamic key (string from backend, not statically known).
     * Use only when the key cannot be a TranslationKey at compile time
     * (e.g. lockedReasonId, errorId from C# DTOs). Prefer `t()` whenever possible.
     */
    const tDynamic = useCallback(
        (key: string, ...args: InterpolationArg[]): string => {
            const value = strings[key];
            if (!value) {
                queueMissingKey("dynamic", key);
                return `[${key}]`;
            }
            return interpolate(value, args);
        },
        [queueMissingKey, strings]
    );

    return { t, tip, tDynamic };
}

/**
 * Get a single translation (non-hook version for one-off usage).
 * Supports interpolation.
 *
 * Note: This reads from the binding snapshot, may not be reactive.
 * Prefer useLocale() hook in components.
 */

