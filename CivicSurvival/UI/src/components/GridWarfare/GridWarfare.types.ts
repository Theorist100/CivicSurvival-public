/**
 * Shared types and utilities for GridWarfare components.
 */

import { formatMoney } from "../../themes";

export const GRID_OPERATION_TYPES = ["drone", "blackout", "disinfo"] as const;
export type GridOperationType = typeof GRID_OPERATION_TYPES[number];

export interface GridWarfareActions {
    prepareOperation: (type: GridOperationType) => void;
    executeOperation: (type: GridOperationType) => void;
    cancelOperation: (type: GridOperationType) => void;
}

/** Format shadow currency amounts: $1.50M / $250k / $500 */
export const formatShadowAmount = (amount: number): string => formatMoney(amount);
