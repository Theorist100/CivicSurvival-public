/**
 * AxisBar - one enemy mirror axis (physical/digital/social), 20-100%.
 * Shows the axis "health" with color coding; lower = weaker enemy on that axis.
 */

import React, { memo, useMemo } from "react";
import { Row } from "../coherent";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles, getPressureColor, getStabilityColor } from "./GridWarfare.styles";
import { useLocale, type TranslationKey } from "../../locales";

const THREAT_LEVELS: Array<{ threshold: number; key: TranslationKey }> = [
    { threshold: 80, key: "UI_GW_THREAT_CRITICAL" },
    { threshold: 60, key: "UI_GW_THREAT_HIGH" },
    { threshold: 40, key: "UI_GW_THREAT_MODERATE" },
];

interface AxisBarProps {
    labelText: string;
    value: number;      // 20-100%
    accentColor: string;
    suppressed?: boolean; // axis is in its post-floor respite window — waves of this type weaken
}

export const AxisBar: React.FC<AxisBarProps> = memo(({ labelText, value, accentColor, suppressed }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();

    const color = getPressureColor(value, accents, theme);

    // Normalize to 0-100 for visual (floor is 20%)
    const visualPercent = Math.max(0, Math.min(100, value));

    const threatKey = THREAT_LEVELS.find(t => value >= t.threshold)?.key ?? "UI_GW_THREAT_LOW";

    return (
        <div style={s.metricBar}>
            {/* Header */}
            <div style={s.metricHeader}>
                <span style={s.metricLabel(accentColor)}>
                    {labelText}
                </span>
                {suppressed && (
                    <span style={{
                        fontSize: "10rem",
                        fontWeight: 700,
                        textTransform: "uppercase" as const,
                        color: accents.operations.accent,
                        marginLeft: "6rem",
                    }}>
                        {l.t("UI_GW_RESPITE_BADGE")}
                    </span>
                )}
                <span style={s.metricValue}>
                    {value.toFixed(0)}%
                </span>
            </div>

            {/* Progress bar */}
            <div style={s.progressBar}>
                <div style={s.progressFill(visualPercent, color)} />
            </div>

            {/* Threat level indicator */}
            <Row justify="space-between" align="center" style={{ marginTop: "4rem" }}>
                <span style={{
                    fontSize: "10rem",
                    color: theme.colors.textMuted,
                }}>
                    {l.t("UI_GW_PRESSURE_FLOOR")}
                </span>
                <span style={{
                    fontSize: "10rem",
                    fontWeight: 700,
                    color,
                }}>
                    {l.t(threatKey)}
                </span>
            </Row>
        </div>
    );
});

AxisBar.displayName = "AxisBar";

// ============================================================================

interface StabilityBarProps {
    stability: number; // 0-100%
    discount: number;  // 0-0.2 (displayed as %)
}

export const StabilityBar: React.FC<StabilityBarProps> = memo(({ stability, discount }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();

    const color = getStabilityColor(stability, accents, theme);
    const discountPercent = Math.round(discount * 100);

    return (
        <div style={s.metricBar}>
            {/* Header */}
            <div style={s.metricHeader}>
                <span style={s.metricLabel(accents.schemes.accent)}>
                    {l.t("UI_GW_STABILITY")}
                </span>
                <span style={s.metricValue}>
                    {stability.toFixed(0)}%
                </span>
            </div>

            {/* Progress bar */}
            <div style={s.progressBar}>
                <div style={s.progressFill(stability, color)} />
            </div>

            {/* Discount indicator */}
            <Row justify="space-between" align="center" style={{ marginTop: "4rem" }}>
                <span style={{
                    fontSize: "10rem",
                    color: theme.colors.textMuted,
                }}>
                    {l.t("UI_GW_DISCOUNT")}
                </span>
                <span style={{
                    fontSize: "10rem",
                    fontWeight: 700,
                    color: accents.schemes.accent,
                }}>
                    -{discountPercent}%
                </span>
            </Row>
        </div>
    );
});

StabilityBar.displayName = "StabilityBar";
