/**
 * Shared duration and percent display helpers.
 *
 * This is intentionally format-only: no bindings, no state ownership.
 */

import { useMemo } from "react";

type PercentInput = "value" | "ratio";
const SECONDS_PER_MINUTE = 60;
const MINUTES_PER_HOUR = 60;
const SECONDS_PER_HOUR = SECONDS_PER_MINUTE * MINUTES_PER_HOUR;

function trimFixed(value: number, decimals: number): string {
    return decimals > 0 ? value.toFixed(decimals) : Math.round(value).toString();
}

export function useDurationFormatter() {
    return useMemo(() => ({
        decimal: (value: number, decimals = 0): string => trimFixed(value, decimals),
        hours: (value: number, decimals = 0): string => `${trimFixed(value, decimals)}h`,
        days: (value: number, decimals = 0): string => {
            const display = trimFixed(value, decimals);
            return `${display} ${display === "1" ? "day" : "days"}`;
        },
        duration: (seconds: number): string => {
            const safeSeconds = Math.max(0, Math.floor(seconds));
            const hours = Math.floor(safeSeconds / SECONDS_PER_HOUR);
            const minutes = Math.floor((safeSeconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);
            const secs = safeSeconds % SECONDS_PER_MINUTE;

            if (hours > 0) return `${hours}h ${minutes}m`;
            if (minutes > 0) return `${minutes}m ${secs}s`;
            return `${secs}s`;
        },
        percent: (value: number, decimals = 0, input: PercentInput = "value"): string => {
            const percentValue = input === "ratio" ? value * 100 : value;
            return `${trimFixed(percentValue, decimals)}%`;
        },
    }), []);
}
