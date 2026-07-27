/**
 * Dashboard navigation requests from outside the Dashboard root.
 *
 * The Dashboard's active domain and the ContentPanel's active view are local component
 * state in their own React tree; surfaces in other roots (the fullscreen War Room) cannot
 * reach them. Mirrors the useWarRoomOverlay pattern: a module-level store bridges the
 * trees, useSyncExternalStore keeps consumers in step.
 *
 * A request is an event, not persistent state — `seq` grows on every navigate() so the
 * same target can be requested twice in a row. The Dashboard applies the domain (and
 * un-minimizes); the ContentPanel applies the view once its domain prop matches and
 * marks the request consumed, so a later remount of either component never re-applies
 * a stale request. Consumption is a plain mutation (no emit): only the apply-effects
 * read the flag, and they are driven by seq/domain changes.
 */

import { useSyncExternalStore } from "react";
import type { DomainId } from "../components/Dashboard/DomainTabs/DomainTabs";
import type { ViewId } from "../components/Dashboard/ContentPanel/ContentPanel.styles";

export interface DashboardNavRequest {
    seq: number;
    domain: DomainId;
    view: ViewId;
    consumed: boolean;
}

let request: DashboardNavRequest | null = null;
const listeners = new Set<() => void>();

const emit = (): void => {
    for (const listener of listeners) listener();
};

export const navigateDashboard = (domain: DomainId, view: ViewId): void => {
    request = { seq: (request?.seq ?? 0) + 1, domain, view, consumed: false };
    emit();
};

export const consumeDashboardNav = (): void => {
    if (request) request.consumed = true;
};

const subscribe = (onStoreChange: () => void): (() => void) => {
    listeners.add(onStoreChange);
    return () => {
        listeners.delete(onStoreChange);
    };
};

const getSnapshot = (): DashboardNavRequest | null => request;

export const useDashboardNavRequest = (): DashboardNavRequest | null =>
    useSyncExternalStore(subscribe, getSnapshot);
