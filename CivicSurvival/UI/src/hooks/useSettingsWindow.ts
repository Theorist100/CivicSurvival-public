/**
 * Settings window open-state — a tiny external store shared across UI roots.
 *
 * The gear lives inside the Dashboard root (GlobalStatus) while the window is its own
 * module-registry root; separate React trees can't share useState, so a module-level store
 * bridges them. Same shape as useWarRoomOverlay.
 *
 * This is the replacement for the old C# flag (SettingsUISystem.IsSettingsExpanded). That
 * flag was the mod's only piece of UI open/closed state living outside React, left over
 * from a time when another system read it to size its own DTO. Nothing reads it now, and
 * because it survived leaving a city it kept re-opening the window over the main menu.
 *
 * The window itself is a module root mounted beside the Dashboard, so it is inside the game
 * UI layer: leaving a city hides it along with everything else. That is what a portal into
 * <body> could not do, and why no separate "is the city loaded" gate is needed here.
 */

import { useSyncExternalStore } from "react";

let isOpen = false;
const listeners = new Set<() => void>();

const emit = (): void => {
    for (const listener of listeners) listener();
};

export const openSettingsWindow = (): void => {
    if (isOpen) return;
    isOpen = true;
    emit();
};

export const closeSettingsWindow = (): void => {
    if (!isOpen) return;
    isOpen = false;
    emit();
};

export const toggleSettingsWindow = (): void => {
    isOpen = !isOpen;
    emit();
};

const subscribe = (onStoreChange: () => void): (() => void) => {
    listeners.add(onStoreChange);
    return () => {
        listeners.delete(onStoreChange);
    };
};

const getSnapshot = (): boolean => isOpen;

export const useSettingsWindowOpen = (): boolean => useSyncExternalStore(subscribe, getSnapshot);
