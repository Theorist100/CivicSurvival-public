import { useMemo } from "react";
import { type BindingState } from "./domain/useDtoBinding";

export type OptionalBindingState<T> =
    | { status: "available"; data: T }
    | { status: "unavailable"; reason: "loading" | "invalid"; error?: string; data?: T };

export function useOptionalBinding<T>(state: BindingState<T>): OptionalBindingState<T> {
    return useMemo(() => {
        if (state.status === "ready") {
            return { status: "available", data: state.data };
        }

        if (state.status === "invalid") {
            if ("data" in state && state.data !== undefined) {
                return { status: "unavailable", reason: "invalid", error: state.error, data: state.data };
            }

            return { status: "unavailable", reason: "invalid", error: state.error };
        }

        if ("data" in state && state.data !== undefined) {
            return { status: "unavailable", reason: "loading", data: state.data };
        }

        return { status: "unavailable", reason: "loading" };
    }, [state]);
}

export function optionalBindingData<T>(state: OptionalBindingState<T>): T | null {
    return state.status === "available" ? state.data : null;
}
