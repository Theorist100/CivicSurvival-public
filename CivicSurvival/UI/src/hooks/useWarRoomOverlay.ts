/**
 * War Room overlay open-state — a tiny external store shared across UI roots.
 *
 * The expand button lives inside the Dashboard root (RADAR tab → WarContent) while the
 * fullscreen overlay is its own module-registry root; separate React trees can't share
 * useState, so a module-level store bridges them. useSyncExternalStore keeps both trees
 * in step. State is intentionally NOT persisted — the War Room always opens closed.
 */

import { useSyncExternalStore } from "react";

let isOpen = false;
const listeners = new Set<() => void>();

const emit = (): void => {
    for (const listener of listeners) listener();
};

export const openWarRoom = (): void => {
    if (isOpen) return;
    isOpen = true;
    emit();
};

export const closeWarRoom = (): void => {
    if (!isOpen) return;
    isOpen = false;
    emit();
};

const subscribe = (onStoreChange: () => void): (() => void) => {
    listeners.add(onStoreChange);
    return () => {
        listeners.delete(onStoreChange);
    };
};

const getSnapshot = (): boolean => isOpen;

export const useWarRoomOpen = (): boolean => useSyncExternalStore(subscribe, getSnapshot);
