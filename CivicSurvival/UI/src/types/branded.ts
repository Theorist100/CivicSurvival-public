declare const __DISPLAY_ONLY: unique symbol;

export type DisplayOnly<T> = { readonly [__DISPLAY_ONLY]: T };
export type ProgressFraction = DisplayOnly<number>;

export function asProgressFraction(value: number): ProgressFraction {
    return Math.max(0, Math.min(1, value)) as unknown as ProgressFraction;
}

export function fractionToPercent(value: ProgressFraction): number {
    return Math.round((value as unknown as number) * 100);
}

export function fractionToWidthPercent(value: ProgressFraction): string {
    return `${fractionToPercent(value)}%`;
}
