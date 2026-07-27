import { type WavePhase } from "../types/dtoSubTypes";

/**
 * Money formatter utility — canonical for standalone display
 */
export const formatMoney = (value: number): string => {
    const sign = value < 0 ? "-" : "";
    const absValue = Math.abs(value);
    if (absValue >= 1_000_000) {
        return `${sign}$${(absValue / 1_000_000).toFixed(2)}M`;
    }
    if (absValue >= 1_000) {
        return `${sign}$${(absValue / 1_000).toFixed(0)}k`;
    }
    return `${sign}$${absValue}`;
};

/**
 * Extract thousands digit for L10N interpolation (e.g., 250000 → "250").
 * L10N strings like "${0}k" add the prefix/suffix themselves.
 */
export const formatCostArg = (value: number): string => (value / 1_000).toFixed(0);

/**
 * Money with a unit picked per value ("2K", "972K", "8.2M"). For sums whose
 * scale varies by orders of magnitude — the shadow ladder spans a few thousand
 * to millions, where a fixed-K scale reads as noise ("$5000K"). L10N strings
 * add the "$" prefix; the unit is part of the returned value.
 */
export const formatMoneyCompact = (value: number): string => {
    const abs = Math.abs(value);
    if (abs >= 1_000_000_000) return `${(value / 1_000_000_000).toFixed(1)}B`;
    // One decimal below 10M ("8.2M"), none above ("120M") — precision where it reads.
    if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1)}M`;
    if (abs >= 1_000) return `${(value / 1_000).toFixed(0)}K`;
    return `${value.toFixed(0)}`;
};

/**
 * Wave phase color mapping — canonical source.
 * Maps phase names to accent colors.
 */
export const getPhaseColors = (
    accents: { schemes: { accent: string }; resilience: { accent: string }; crisis: { accent: string }; operations: { accent: string } },
    psyColor: string,
): Record<WavePhase, string> => ({
    calm: accents.schemes.accent,
    alert: accents.resilience.accent,
    attack: accents.crisis.accent,
    // Info-war raid — purple, distinct from the red kinetic attack.
    psyattack: psyColor,
    recovery: accents.operations.accent,
});
