/**
 * Color utility functions for Coherent UI compatibility.
 *
 * Coherent UI's Chromium fork doesn't support 8-digit hex (#RRGGBBAA).
 * Use hexToRgba() to create transparent colors instead.
 */

/**
 * Convert hex (#RRGGBB) or rgba() + alpha to rgba() string.
 * If input is already rgba(), replaces the alpha channel.
 *
 * @example
 *   hexToRgba("#4488cc", 0.12)               → "rgba(68, 136, 204, 0.12)"
 *   hexToRgba("rgba(255, 255, 255, 0.5)", 0.12) → "rgba(255, 255, 255, 0.12)"
 */
export function hexToRgba(color: string, alpha: number): string {
    const safeAlpha = Number.isFinite(alpha) ? Math.max(0, Math.min(1, alpha)) : 1;

    if (!color)
        return `rgba(0, 0, 0, ${safeAlpha})`;

    if (color.startsWith("rgba(") || color.startsWith("rgb(")) {
        const match = color.match(/(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        if (match) {
            return `rgba(${match[1]}, ${match[2]}, ${match[3]}, ${safeAlpha})`;
        }
    }

    if (color === "transparent")
        return "rgba(0, 0, 0, 0)";

    if (/^#[0-9a-fA-F]{3}$/.test(color)) {
        const r = Number.parseInt(color.charAt(1) + color.charAt(1), 16);
        const g = Number.parseInt(color.charAt(2) + color.charAt(2), 16);
        const b = Number.parseInt(color.charAt(3) + color.charAt(3), 16);
        return `rgba(${r}, ${g}, ${b}, ${safeAlpha})`;
    }

    if (/^#[0-9a-fA-F]{6}$/.test(color)) {
        const r = Number.parseInt(color.slice(1, 3), 16);
        const g = Number.parseInt(color.slice(3, 5), 16);
        const b = Number.parseInt(color.slice(5, 7), 16);
        return `rgba(${r}, ${g}, ${b}, ${safeAlpha})`;
    }

    return `rgba(0, 0, 0, ${safeAlpha})`;
}

function toLinear(channel: number): number {
    const value = channel / 255;
    return value <= 0.03928
        ? value / 12.92
        : Math.pow((value + 0.055) / 1.055, 2.4);
}

export function relativeLuminance(hex: string): number {
    if (!/^#[0-9a-fA-F]{6}$/.test(hex)) {
        return 0;
    }

    const r = toLinear(Number.parseInt(hex.slice(1, 3), 16));
    const g = toLinear(Number.parseInt(hex.slice(3, 5), 16));
    const b = toLinear(Number.parseInt(hex.slice(5, 7), 16));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function getReadableForeground(bg: string): string {
    return relativeLuminance(bg) > 0.55 ? "#1a1a1a" : "#ffffff";
}
