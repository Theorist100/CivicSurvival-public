/**
 * Safe binding utilities for Coherent UI initialization.
 *
 * Problem: useValue() returns {} before C# bindings are ready.
 * This is NOT a bug - it's Coherent UI architecture.
 *
 * Solution: Explicit isReady state + logging, NOT silent fallbacks.
 */

import { useMemo, useRef, useEffect } from "react";
import { useValue } from "cs2/api";
import { scLog } from "../utils/logging";
import { safeJsonParse } from "../utils/jsonParse";
import { PARSE_PROFILING_ENABLED, parseNow, recordParse } from "../utils/parseProfiler";
import { type BindingState } from "./domain/useDtoBinding";

// CS2 binding type (inferred from bindValue)
type AnyBinding = Parameters<typeof useValue>[0];

interface BindingOptions {
    debugName?: string;
}

/**
 * Check if useValue result is the "not ready" empty object {}.
 */
function isBindingReady(value: unknown): boolean {
    if (value == null) return false;
    // Empty arrays ARE valid data — only plain empty objects {} indicate not-ready
    if (typeof value === "object" && !Array.isArray(value) && Object.keys(value as object).length === 0) return false;
    return true;
}

const previewValue = (value: unknown): string => {
    if (typeof value === "string") return value.slice(0, 200);
    try {
        return JSON.stringify(value).slice(0, 200);
    } catch {
        return String(value).slice(0, 200);
    }
};

const invalidBindingState = <T,>(value: unknown, error: string): BindingState<T> => ({
    status: "invalid",
    preview: previewValue(value),
    error,
});

/**
 * Parse JSON array with explicit ready state.
 * Returns { data, isReady } so component can decide what to do.
 *
 * @example
 * const { data: threats, isReady } = useSafeJsonArray(radarThreats$);
 * if (!isReady) return null; // Don't render until ready
 */
export function useSafeJsonArrayWithState(
    binding: AnyBinding,
    debugName?: string
): { data: unknown[]; isReady: boolean } {
    const value = useValue(binding);
    const hasLoggedNotReady = useRef(false);
    const hasLoggedReady = useRef(false);

    const isReady = isBindingReady(value);

    // Log state transitions for debugging
    useEffect(() => {
        if (!isReady && !hasLoggedNotReady.current) {
            scLog(`useSafeBinding: ${debugName ?? "array"} NOT READY (value=${JSON.stringify(value)})`);
            hasLoggedNotReady.current = true;
        }
        if (isReady && !hasLoggedReady.current) {
            scLog(`useSafeBinding: ${debugName ?? "array"} READY`);
            hasLoggedReady.current = true;
        }
    }, [isReady, value, debugName]);

    const data = useMemo(() => {
        if (!isReady) return [];

        const json = value as string;
        if (json === "" || json === "[]") return [];

        try {
            if (!PARSE_PROFILING_ENABLED) {
                const parsed: unknown = JSON.parse(json);
                return Array.isArray(parsed) ? parsed : [];
            }
            const t0 = parseNow();
            const parsed: unknown = JSON.parse(json);
            const t1 = parseNow();
            const ok = Array.isArray(parsed);
            const t2 = parseNow();
            recordParse(debugName ?? "array", t1 - t0, t2 - t1);
            return ok ? parsed : [];
        } catch (e) {
            scLog(`useSafeBinding: ${debugName ?? "array"} PARSE ERROR: ${e}`);
            return [];
        }
    }, [value, isReady, debugName]);

    return { data, isReady };
}

export function useValidatedJsonArray<T>(
    binding: AnyBinding,
    validateItem: (value: unknown) => value is T,
    options: BindingOptions = {}
): BindingState<T[]> {
    const value = useValue(binding);
    const debugName = options.debugName ?? "json_array";

    return useMemo(() => {
        if (!isBindingReady(value)) return { status: "loading" };
        if (typeof value !== "string") {
            return invalidBindingState(value, `${debugName} binding is not a JSON string`);
        }
        if (value === "" || value === "[]") return { status: "ready", data: [] };

        const parsed = safeJsonParse(value, Array.isArray, debugName);
        if (!parsed) {
            return invalidBindingState(value, `${debugName} failed JSON array validation`);
        }

        const result: T[] = [];
        for (const item of parsed) {
            if (!validateItem(item)) {
                return invalidBindingState(value, `${debugName} contains an invalid item`);
            }
            result.push(item);
        }

        return { status: "ready", data: result };
    }, [value, validateItem, debugName]);
}

// ============ Primitive value wrappers ============

/**
 * Safe wrapper for primitive bindings (number, boolean, string).
 * Returns fallback if binding returns {} (not ready).
 *
 * @example
 * const count = useSafeNumber(someCount$, 0);
 * const debugMode = useSafeBoolean(debugMode$, false);
 * const status = useSafeString(status$, "unknown");
 */

export function useSafeNumber(
    binding: AnyBinding,
    fallback: number,
    debugName?: string
): number {
    const value = useValue(binding);
    const hasLoggedNotReady = useRef(false);

    const isReady = typeof value === "number";

    useEffect(() => {
        if (!isReady && !hasLoggedNotReady.current) {
            const name = debugName || "unnamed_number";
            scLog(`[SAFE BINDING] ${name} NOT READY (got ${typeof value}, value=${JSON.stringify(value)})`);
            hasLoggedNotReady.current = true;
        }
    }, [isReady, value, debugName]);

    return isReady ? value : fallback;
}

export function usePrimitiveBinding<T extends string | number | boolean>(
    binding: AnyBinding,
    validate: (value: unknown) => value is T,
    options: BindingOptions = {}
): BindingState<T> {
    const value = useValue(binding);
    const debugName = options.debugName ?? "primitive";

    return useMemo(() => {
        if (!isBindingReady(value)) return { status: "loading" };
        if (validate(value)) return { status: "ready", data: value };
        return invalidBindingState(value, `${debugName} failed primitive validation`);
    }, [value, validate, debugName]);
}

const isNumber = (value: unknown): value is number => typeof value === "number";
const isBoolean = (value: unknown): value is boolean => typeof value === "boolean";
const isString = (value: unknown): value is string => typeof value === "string";

export const useNumberBinding = (binding: AnyBinding, debugName?: string): BindingState<number> =>
    usePrimitiveBinding(binding, isNumber, debugName ? { debugName } : {});

export const useBooleanBinding = (binding: AnyBinding, debugName?: string): BindingState<boolean> =>
    usePrimitiveBinding(binding, isBoolean, debugName ? { debugName } : {});

export const useStringBinding = (binding: AnyBinding, debugName?: string): BindingState<string> =>
    usePrimitiveBinding(binding, isString, debugName ? { debugName } : {});

export function useSafeBoolean(
    binding: AnyBinding,
    fallback: boolean,
    debugName?: string
): boolean {
    const value = useValue(binding);
    const hasLoggedNotReady = useRef(false);

    const isReady = typeof value === "boolean";

    useEffect(() => {
        if (!isReady && !hasLoggedNotReady.current) {
            const name = debugName || "unnamed_boolean";
            scLog(`[SAFE BINDING] ${name} NOT READY (got ${typeof value}, value=${JSON.stringify(value)})`);
            hasLoggedNotReady.current = true;
        }
    }, [isReady, value, debugName]);

    return isReady ? value : fallback;
}

export function useSafeString(
    binding: AnyBinding,
    fallback: string,
    debugName?: string
): string {
    const value = useValue(binding);
    const hasLoggedNotReady = useRef(false);

    const isReady = typeof value === "string";

    useEffect(() => {
        if (!isReady && !hasLoggedNotReady.current) {
            const name = debugName || "unnamed_string";
            scLog(`[SAFE BINDING] ${name} NOT READY (got ${typeof value}, value=${JSON.stringify(value)})`);
            hasLoggedNotReady.current = true;
        }
    }, [isReady, value, debugName]);

    return isReady ? value : fallback;
}

// ============ Convenience wrappers (use when you KNOW it's safe) ============

/**
 * CONVENIENCE: Returns just data, logs if not ready.
 * Use when component can handle empty array gracefully.
 */
export function useSafeJsonArray(binding: AnyBinding, fallback: unknown[] = [], debugName?: string): unknown[] {
    const { data, isReady } = useSafeJsonArrayWithState(binding, debugName);
    // If not ready, return fallback but it's LOGGED above
    return isReady ? data : fallback;
}


