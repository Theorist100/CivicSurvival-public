/**
 * Build-mode of the DEFENSE tab's construction section: air-defense barrels or strike
 * launchers. Lives outside the component so the War Room's BUILD button (another React
 * root) can pre-select STRIKE before navigating the Dashboard to the tab, and so the
 * chosen mode survives the tab unmounting on a domain switch. Not persisted — a fresh
 * session opens on DEFENSE.
 */

import { useSyncExternalStore } from "react";

export type DefenseBuildMode = "defense" | "strike";

let mode: DefenseBuildMode = "defense";
const listeners = new Set<() => void>();

const emit = (): void => {
    for (const listener of listeners) listener();
};

export const setDefenseBuildMode = (next: DefenseBuildMode): void => {
    if (mode === next) return;
    mode = next;
    emit();
};

const subscribe = (onStoreChange: () => void): (() => void) => {
    listeners.add(onStoreChange);
    return () => {
        listeners.delete(onStoreChange);
    };
};

const getSnapshot = (): DefenseBuildMode => mode;

export const useDefenseBuildMode = (): DefenseBuildMode => useSyncExternalStore(subscribe, getSnapshot);
