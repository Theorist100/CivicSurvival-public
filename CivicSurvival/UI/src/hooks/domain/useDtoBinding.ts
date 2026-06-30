import { useMemo } from "react";
import { useValue, type ValueBinding } from "cs2/api";
import { safeJsonParse } from "../../utils/jsonParse";
import { scError } from "../../utils/logging";

// Diagnostic: log the first rejected payload per binding once (publishes change
// every tick — dedupe by name, not payload). Identifies which binding's live
// JSON fails its generated type guard so the failing field can be pinpointed.
const s_loggedInvalidBindings = new Set<string>();

export type BindingState<T> =
    | { status: "loading"; data?: T }
    | { status: "invalid"; preview: string; error: string; data?: T }
    | { status: "ready"; data: T };

export type AnyValueBinding = ValueBinding<unknown> | ValueBinding<string>;

interface UseDtoBindingOptions<T> {
    debugName?: string;
    defaultValue?: T;
}

export function useDtoBinding<T>(
    binding: AnyValueBinding,
    validate: (value: unknown) => value is T,
    options: UseDtoBindingOptions<T> = {}
): BindingState<T> {
    const raw = useValue(binding);

    return useMemo(() => {
        if (typeof raw !== "string" || raw === "" || raw === "{}") {
            if (options.defaultValue !== undefined) {
                return { status: "loading", data: options.defaultValue };
            }

            return { status: "loading" };
        }

        const data = safeJsonParse(raw, validate, options.debugName);
        if (data !== null) {
            return { status: "ready", data };
        }

        const name = options.debugName ?? "(anonymous binding)";
        if (!s_loggedInvalidBindings.has(name)) {
            s_loggedInvalidBindings.add(name);
            scError(`[DTO-INVALID] binding '${name}' rejected by type guard. Raw payload: ${raw}`);
        }

        if (options.defaultValue !== undefined) {
            // Contract skew: the guard rejected a non-empty payload. Vanilla
            // invariant — hand back a renderable value, never blank. The
            // [DTO-INVALID] log above already fired (once per binding), so the
            // skew stays loud; prebuild contract-checks are the primary line.
            return {
                status: "invalid",
                preview: raw.slice(0, 200),
                error: options.debugName
                    ? `${options.debugName} failed DTO validation`
                    : "DTO validation failed",
                data: options.defaultValue,
            };
        }

        return {
            status: "invalid",
            preview: raw.slice(0, 200),
            error: options.debugName
                ? `${options.debugName} failed DTO validation`
                : "DTO validation failed",
        };
    }, [raw, validate, options.debugName, options.defaultValue]);
}

export function mapBindingState<T, U>(
    state: BindingState<T>,
    map: (value: T) => U
): BindingState<U> {
    if (state.status === "ready") {
        return { status: "ready", data: map(state.data) };
    }

    if (state.status === "loading") {
        return state.data === undefined
            ? { status: "loading" }
            : { status: "loading", data: map(state.data) };
    }

    return state.data === undefined
        ? { status: "invalid", preview: state.preview, error: state.error }
        : { status: "invalid", preview: state.preview, error: state.error, data: map(state.data) };
}

// Returns `T` — never null. `loading` AND `invalid` both fall back to the
// non-null default or the state-carried data, so callers have nothing to
// null-check and MUST NOT blank themselves with `return null`. A renderable
// value must not erase the observable not-ready status.
export function bindingDataOrDefault<T>(state: BindingState<T>, defaultValue: T): T {
    if (state.status === "ready") return state.data;
    if ("data" in state && state.data !== undefined) return state.data;
    return defaultValue;
}
