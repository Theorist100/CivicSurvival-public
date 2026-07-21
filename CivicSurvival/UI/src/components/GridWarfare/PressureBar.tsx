/**
 * AxisBar - one enemy mirror axis (physical/digital/social), 20-100%, rendered as the
 * War Room enemy-strip gauge: axis label + optional REGROUP badge + big value on top,
 * a color-coded health track, and a strike-type / threat-level sub-row. Lower value =
 * weaker enemy on that axis. Flex-only, mono-typed to read as the command console.
 */

import React, { memo, useMemo } from "react";
import { Row } from "../coherent";
import { useTheme, useAccents, hexToRgba } from "../../themes";
import { createGridWarfareStyles, getPressureColor, getStabilityColor } from "./GridWarfare.styles";
import { useLocale, type TranslationKey } from "../../locales";

const THREAT_LEVELS: Array<{ threshold: number; key: TranslationKey }> = [
    { threshold: 80, key: "UI_GW_THREAT_CRITICAL" },
    { threshold: 60, key: "UI_GW_THREAT_HIGH" },
    { threshold: 40, key: "UI_GW_THREAT_MODERATE" },
];

interface AxisBarProps {
    labelText: string;
    value: number;      // 20-100% (already 25%-quantized by the producer when coarse)
    accentColor: string;
    suppressed?: boolean;  // axis is in its post-floor respite window — waves of this type weaken
    strikeLabel: string;   // sub-row left caption, e.g. "KINETIC STRIKES"
    regroupLabel: string;  // badge shown while the axis is in respite
    coarse?: boolean;      // low-intel view: value is a rounded estimate, not an exact read
    respiteHoursLeft?: number; // full-intel respite countdown in game-hours; < 0 = hidden
}

export const AxisBar: React.FC<AxisBarProps> = memo(({ labelText, value, accentColor, suppressed, strikeLabel, regroupLabel, coarse, respiteHoursLeft }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const color = getPressureColor(value, accents, theme);
    const visualPercent = Math.max(0, Math.min(100, value));
    const threatKey = THREAT_LEVELS.find(t => value >= t.threshold)?.key ?? "UI_GW_THREAT_LOW";
    const mono = theme.typography.fontFamilyMono;
    // Coarse Lv0 read shows an approximate value ("~50%"); an exact read shows the number.
    const valueText = coarse ? `~${value.toFixed(0)}%` : `${value.toFixed(0)}%`;
    // Full-intel respite countdown rides inside the badge (e.g. "REGROUP 4h").
    const showRespiteTimer = respiteHoursLeft !== undefined && respiteHoursLeft >= 0;
    const respiteText = showRespiteTimer
        ? `${regroupLabel} ${Math.max(1, Math.round(respiteHoursLeft as number))}h`
        : regroupLabel;

    const gaugeStyle: React.CSSProperties = {
        display: "flex",
        flexDirection: "column" as const,
        flex: 1,
        minWidth: 0,
        minHeight: "40rem",
        padding: "8rem 12rem",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(theme.colors.surface, 0.6),
        border: `2rem solid ${hexToRgba(accentColor, 0.4)}`,
    };

    return (
        <div style={gaugeStyle}>
            <Row align="center" justify="space-between">
                <Row align="center" gap="8rem">
                    <span style={{
                        fontSize: "12rem",
                        fontWeight: 700,
                        textTransform: "uppercase" as const,
                        letterSpacing: "0.5rem",
                        color: accentColor,
                        fontFamily: mono,
                    }}>
                        {labelText}
                    </span>
                    {suppressed && (
                        <span style={{
                            padding: "2rem 6rem",
                            borderRadius: "3rem",
                            fontSize: "10rem",
                            fontWeight: 700,
                            textTransform: "uppercase" as const,
                            color: accents.schemes.accent,
                            background: hexToRgba(accents.schemes.accent, 0.15),
                            border: `1rem solid ${hexToRgba(accents.schemes.accent, 0.5)}`,
                            fontFamily: mono,
                        }}>
                            {respiteText}
                        </span>
                    )}
                </Row>
                <span style={{
                    fontSize: "24rem",
                    fontWeight: 700,
                    lineHeight: 1,
                    color,
                    fontFamily: mono,
                }}>
                    {valueText}
                </span>
            </Row>

            <div style={{
                height: "8rem",
                marginTop: "8rem",
                borderRadius: "4rem",
                background: hexToRgba(theme.colors.textMuted, 0.15),
                overflow: "hidden" as const,
            }}>
                <div style={{
                    width: `${visualPercent}%`,
                    height: "100%",
                    background: color,
                    boxShadow: `0 0 10rem ${hexToRgba(color, 0.5)}`,
                }} />
            </div>

            <Row justify="space-between" align="center" style={{ marginTop: "5rem" }}>
                <span style={{
                    fontSize: "10rem",
                    textTransform: "uppercase" as const,
                    letterSpacing: "0.5rem",
                    color: theme.colors.textMuted,
                    fontFamily: mono,
                }}>
                    {strikeLabel}
                </span>
                <span style={{
                    fontSize: "10rem",
                    fontWeight: 700,
                    textTransform: "uppercase" as const,
                    color: coarse ? theme.colors.textMuted : color,
                    fontFamily: mono,
                }}>
                    {coarse ? l.t("UI_WARROOM_LOW_CONFIDENCE") : l.t(threatKey)}
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
