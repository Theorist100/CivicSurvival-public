/**
 * CityOpinionLine — thin 3-segment whole-city mix (you / undecided / enemy).
 * Axis-invariant: the city's opinion doesn't change with the grouping lens.
 */

import React, { memo } from "react";
import { Row } from "../../../../coherent";
import { useTheme } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { type CityOpinion, formatHouseholds } from "./opinionData";
import { Sep } from "./Sep";

interface CityOpinionLineProps {
    opinion: CityOpinion;
    /** Whole-city population — folded into this line's label so the board no
     *  longer needs a separate "CITY 63K" slot on the control strip above. */
    cityCount?: number;
    /** Forwarded onto the root so the Flex/Column gap emulation (which injects a
     *  marginBottom via cloneElement) actually lands — a custom component that
     *  drops `style` silently swallows the gap. */
    style?: React.CSSProperties;
}

const seg = (color: string, pct: number): React.CSSProperties => ({
    width: `${pct}%`,
    backgroundColor: color,
    height: "100%",
});

export const CityOpinionLine = memo(({ opinion, cityCount, style }: CityOpinionLineProps) => {
    const theme = useTheme();
    const l = useLocale();

    return (
        <Row align="center" gap={theme.spacing.sm} style={{ width: "100%", ...style }}>
            <span style={{
                fontSize: theme.typography.sizeXS,
                fontFamily: theme.typography.fontFamilyMono,
                color: theme.colors.textMuted,
                textTransform: "uppercase",
                letterSpacing: "0.6rem",
                minWidth: "92rem",
                whiteSpace: "nowrap",
            }}>
                {cityCount !== undefined && (
                    <>
                        <span style={{ color: theme.colors.textSecondary, fontWeight: 700 }}>
                            {`${l.t("UI_OB_CITY")} ${formatHouseholds(cityCount)}`}
                            {/* Unit spelled out: the count is households, not people. */}
                            <span style={{ color: theme.colors.textMuted, marginLeft: "5rem" }}>
                                {l.t("UI_OB_HOUSEHOLDS")}
                            </span>
                        </span>
                        <Sep />
                    </>
                )}
                {l.t("UI_OB_CITY_OPINION")}
            </span>
            <div style={{
                display: "flex",
                flex: 1,
                height: "8rem",
                borderRadius: "4rem",
                overflow: "hidden",
                backgroundColor: theme.colors.borderLight,
            }}>
                <div style={seg(theme.colors.success, opinion.you)} />
                <div style={seg(theme.colors.textMuted, opinion.neu)} />
                <div style={seg(theme.colors.error, opinion.enemy)} />
            </div>
            <span style={{
                fontSize: theme.typography.sizeXS,
                fontFamily: theme.typography.fontFamilyMono,
                fontWeight: 700,
                color: theme.colors.textSecondary,
                minWidth: "108rem",
                textAlign: "right",
            }}>
                {/* No separator dots — the three figures are already colour-coded
                    (loyal / undecided / enemy) and follow the coloured bar, so a gap
                    reads cleaner than punctuation between them. */}
                <span style={{ color: theme.colors.success }}>{opinion.you}%</span>
                <span style={{ color: theme.colors.textMuted, marginLeft: "8rem" }}>{opinion.neu}%</span>
                <span style={{ color: theme.colors.error, marginLeft: "8rem" }}>{opinion.enemy}%</span>
            </span>
        </Row>
    );
});

CityOpinionLine.displayName = "CityOpinionLine";
