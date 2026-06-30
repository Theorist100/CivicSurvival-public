import { type BindingState } from "./useDtoBinding";

type ReadyValues<T extends Record<string, BindingState<unknown>>> = {
    [K in keyof T]: T[K] extends BindingState<infer U> ? U : never;
};

export function combineBindingStates<T extends Record<string, BindingState<unknown>>, U>(
    states: T,
    map: (values: ReadyValues<T>) => U
): BindingState<U> {
    for (const state of Object.values(states)) {
        if (state.status === "loading") return { status: "loading" };
    }

    for (const state of Object.values(states)) {
        if (state.status === "invalid") {
            return { status: "invalid", preview: state.preview, error: state.error };
        }
    }

    const readyValues: Record<string, unknown> = {};
    for (const [key, state] of Object.entries(states)) {
        readyValues[key] = (state as { status: "ready"; data: unknown }).data;
    }

    return { status: "ready", data: map(readyValues as ReadyValues<T>) };
}
