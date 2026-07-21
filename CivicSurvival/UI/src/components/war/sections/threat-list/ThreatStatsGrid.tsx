import React, { useMemo } from "react";
import { Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale, type TranslationKey } from "@locales";
import { type ThreatListSectionProps } from "../ThreatListSection";
import { StatRow } from "@shared/ui";

interface StatCardDef {
    labelKey: TranslationKey;
    getValue: (p: ThreatListSectionProps) => number;
    getColor: (p: ThreatListSectionProps, accents: ReturnType<typeof useAccents>, theme: ReturnType<typeof useTheme>) => string;
}

interface ThreatStatsGridProps {
    sectionProps: ThreatListSectionProps;
    styles: {
        statCard: (marginSide: "left" | "right") => React.CSSProperties;
    };
}

const hasWaveDataForStats = (status: ThreatListSectionProps["waveDataStatus"]): boolean =>
    status === "active" || status === "completed";

export const ThreatStatsGrid: React.FC<ThreatStatsGridProps> = ({ sectionProps, styles: s }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const hasWaveData = hasWaveDataForStats(sectionProps.waveDataStatus);

    const statCards: Array<[StatCardDef, StatCardDef]> = useMemo(() => [
        [
            {
                labelKey: "THREAT_STAT_SPAWNED",
                getValue: (p) => p.spawned,
                getColor: (_p, _a, t) => t.colors.textPrimary,
            },
            {
                labelKey: "THREAT_STAT_ACTIVE",
                getValue: (p) => p.active,
                getColor: (p, a, t) => p.active > 0 ? a.crisis.accent : t.colors.textPrimary,
            },
        ],
        [
            {
                labelKey: "THREAT_STAT_INTERCEPTED",
                getValue: (p) => p.intercepted,
                getColor: (_p, a) => a.schemes.accent,
            },
            {
                labelKey: "THREAT_STAT_CRASHED",
                getValue: (p) => p.crashed,
                getColor: (p, a, t) => p.crashed > 0 ? a.resilience.accent : t.colors.textMuted,
            },
        ],
        [
            {
                labelKey: "THREAT_STAT_HITS",
                getValue: (p) => p.hits,
                getColor: (p, a, t) => p.hits > 0 ? a.crisis.accent : t.colors.textMuted,
            },
            {
                labelKey: "THREAT_STAT_INTERCEPT",
                getValue: () => -1,
                getColor: (p, a, t) =>
                    !hasWaveDataForStats(p.waveDataStatus)
                        ? t.colors.textMuted
                        : p.interceptRate >= 80 ? a.schemes.accent
                        : p.interceptRate >= 50 ? a.resilience.accent
                        : a.crisis.accent,
            },
        ],
    ], []);

    return (
        <>
            {statCards.map(([left, right], rowIdx) => (
                <Row key={left.labelKey} style={rowIdx < 2 ? { marginBottom: theme.spacing.sm } : undefined}>
                    <StatRow
                        compact
                        label={l.t(left.labelKey)}
                        value={left.getValue(sectionProps)}
                        color={left.getColor(sectionProps, accents, theme)}
                        style={{
                            ...s.statCard("right"),
                            flexDirection: "column",
                            justifyContent: "center",
                            minHeight: "0",
                        }}
                        labelStyle={{ minWidth: 0, fontSize: "10rem", textAlign: "center" }}
                        valueStyle={{ fontSize: "18rem" }}
                    />
                    <StatRow
                        compact
                        label={l.t(right.labelKey)}
                        value={right.labelKey === "THREAT_STAT_INTERCEPT"
                            ? (!hasWaveData ? "--%" : `${sectionProps.interceptRate}%`)
                            : right.getValue(sectionProps)
                        }
                        color={right.getColor(sectionProps, accents, theme)}
                        style={{
                            ...s.statCard("left"),
                            flexDirection: "column",
                            justifyContent: "center",
                            minHeight: "0",
                        }}
                        labelStyle={{ minWidth: 0, fontSize: "10rem", textAlign: "center" }}
                        valueStyle={{ fontSize: "18rem" }}
                    />
                </Row>
            ))}
        </>
    );
};
