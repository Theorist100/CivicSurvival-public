/**
 * EnemyFocusSection - Enemy targeting preferences (Generation, Substations, Residential)
 * Intel domain → Left column
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { createWarViewsStyles } from "../../../Dashboard/ContentPanel/WarViews.styles";
import type { CSSProperties } from "react";
import { type FocusRangeDto } from "../../../../types/domainDtos.generated";

// Re-export under the historical local name so existing imports keep working.
export type FocusRange = FocusRangeDto;

export interface EnemyFocusSectionProps {
    energyFocusRange: FocusRangeDto | null;
    infraFocusRange: FocusRangeDto | null;
    residentialFocusRange: FocusRangeDto | null;
}

const formatRange = (range: FocusRangeDto | null): string => {
    if (!range) return "0%";
    if (range.Min === range.Max) return `${range.Min}%`;
    return `${range.Min}-${range.Max}%`;
};

const getAverage = (range: FocusRangeDto | null): number => {
    if (!range) return 0;
    return (range.Min + range.Max) / 2;
};

const focusValueStyle = (theme: ReturnType<typeof useTheme>, color: string): CSSProperties => ({
    minWidth: "45rem",
    fontFamily: theme.typography.fontFamilyMono,
    fontWeight: 700,
    color,
    textAlign: "right",
});

export const EnemyFocusSection: React.FC<EnemyFocusSectionProps> = memo(({
    energyFocusRange,
    infraFocusRange,
    residentialFocusRange,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);

    return (
        <div style={s.section}>
            <div style={s.sectionTitleColored(accents.crisis.accent)}>
                {l.t("INTEL_ENEMY_FOCUS")}
            </div>

            <div style={s.targetRow}>
                <span style={s.targetName}>{l.t("INTEL_TARGET_GENERATION")}</span>
                <div style={s.targetBar}>
                    <div style={s.targetFill(getAverage(energyFocusRange))} />
                </div>
                <HoverTip text={l.t("TIP_FOCUS_GENERATION")} style={focusValueStyle(theme, accents.crisis.accent)}>
                    {formatRange(energyFocusRange)}
                </HoverTip>
            </div>

            <div style={s.targetRow}>
                <span style={s.targetName}>{l.t("INTEL_TARGET_SUBSTATION")}</span>
                <div style={s.targetBar}>
                    <div style={s.targetFill(getAverage(infraFocusRange))} />
                </div>
                <HoverTip text={l.t("TIP_FOCUS_INFRASTRUCTURE")} style={focusValueStyle(theme, accents.resilience.accent)}>
                    {formatRange(infraFocusRange)}
                </HoverTip>
            </div>

            <div style={s.targetRow}>
                <span style={s.targetName}>{l.t("INTEL_TARGET_RESIDENTIAL")}</span>
                <div style={s.targetBar}>
                    <div style={s.targetFill(getAverage(residentialFocusRange))} />
                </div>
                <HoverTip text={l.t("TIP_FOCUS_RESIDENTIAL")} style={focusValueStyle(theme, accents.schemes.accent)}>
                    {formatRange(residentialFocusRange)}
                </HoverTip>
            </div>
        </div>
    );
});

EnemyFocusSection.displayName = "EnemyFocusSection";
