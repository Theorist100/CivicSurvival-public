/**
 * TensionSection - Tension level, status bar, intel description
 * Intel domain → Left column
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { ProgressBar, StatRow } from "../../../shared/ui";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { createWarViewsStyles, getThreatColorByStatus } from "../../../Dashboard/ContentPanel/WarViews.styles";
import { type TensionStatus } from "../../../../types/domainDtos";

export interface TensionSectionProps {
    tensionLevel: number;
    tensionStatus: TensionStatus;
}

export const TensionSection: React.FC<TensionSectionProps> = memo(({
    tensionLevel,
    tensionStatus,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);

    const threatColor = getThreatColorByStatus(tensionStatus, accents, theme);

    // Intel description follows the backend status band exactly.
    const intelDescription = useMemo(() => {
        switch (tensionStatus) {
            case "CRITICAL": return l.t("INTEL_DESC_CRITICAL");
            case "HIGH": return l.t("INTEL_DESC_HIGH");
            case "ELEVATED": return l.t("INTEL_DESC_ELEVATED");
            case "LOW":
            default: return l.t("INTEL_DESC_LOW");
        }
    }, [tensionStatus, l]);

    return (
        <div style={s.section}>
            <div style={s.sectionTitleColored(threatColor)}>
                {l.t("INTEL_TENSION_LEVEL")}
            </div>
            <StatRow
                label={l.t("INTEL_STATUS")}
                value={<HoverTip text={l.t("TIP_TENSION")}>{tensionStatus}</HoverTip>}
                color={threatColor}
                emphasis="title"
            />
            <ProgressBar value={tensionLevel} color={threatColor} height="10rem" />
            <div style={s.assessment(threatColor)}>
                {l.t("UI_INTEL_PREFIX", intelDescription)}
            </div>
        </div>
    );
});

TensionSection.displayName = "TensionSection";
