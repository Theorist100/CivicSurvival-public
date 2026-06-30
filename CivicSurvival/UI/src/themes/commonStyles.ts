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
 * Wave phase color mapping — canonical source.
 * Maps phase names to accent colors.
 */
export const getPhaseColors = (accents: { schemes: { accent: string }; resilience: { accent: string }; crisis: { accent: string }; operations: { accent: string } }): Record<string, string> => ({
    calm: accents.schemes.accent,
    alert: accents.resilience.accent,
    attack: accents.crisis.accent,
    recovery: accents.operations.accent,
});
