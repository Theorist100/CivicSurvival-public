/**
 * AttackForecastSection - Attack type, ETA, composition predictions
 * Intel domain → Right column
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { StatRow } from "../../../shared/ui";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { createWarViewsStyles } from "../../../Dashboard/ContentPanel/WarViews.styles";
import { type AttackTimeEstimateDto } from "../../../../types/domainDtos.generated";

// Re-export under the historical local name so existing imports keep working.
export type AttackTimeEstimate = AttackTimeEstimateDto;

export interface AttackForecastSectionProps {
    waveTypePrediction: string | null;
    isMassiveStrike: boolean;
    timeEstimate: AttackTimeEstimateDto | null;
    threatComposition: string | null;
    estimatedShaheds: number;
    estimatedBallistics: number;
    hasInsiderInfo: boolean;
}

export const AttackForecastSection: React.FC<AttackForecastSectionProps> = memo(({
    waveTypePrediction,
    isMassiveStrike,
    timeEstimate,
    threatComposition,
    estimatedShaheds,
    estimatedBallistics,
    hasInsiderInfo,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);

    const formatEta = (): string => {
        if (!timeEstimate) return "?";
        if (timeEstimate.Status === "unknown") return l.t("UI_INTEL_ETA_UNKNOWN");
        if (timeEstimate.Status === "in-attack") return l.t("UI_INTEL_ETA_DURING_ATTACK");
        if (timeEstimate.Status === "in-recovery") return l.t("UI_INTEL_ETA_RECOVERY");
        if (timeEstimate.Status === "awaiting-window") return l.t("UI_INTEL_ETA_AWAITING_WINDOW");
        if (hasInsiderInfo) {
            return `${timeEstimate.MinHours?.toFixed(1) ?? "?"}h`;
        }
        return `${timeEstimate.MinHours?.toFixed(0) ?? "?"}-${timeEstimate.MaxHours?.toFixed(0) ?? "?"}h`;
    };

    return (
        <div style={s.section}>
            <div style={s.sectionTitleColored(accents.crisis.accent)}>
                {l.t("INTEL_ATTACK_FORECAST")}
            </div>

            <StatRow
                label={l.t("INTEL_WAVE_TYPE")}
                value={<HoverTip text={l.t("TIP_WAVE_TYPE")}>{waveTypePrediction ?? l.t("INTEL_UNKNOWN")}</HoverTip>}
                color={isMassiveStrike ? accents.crisis.accent : theme.colors.textSecondary}
                emphasis="title"
            />

            <StatRow
                label={l.t("INTEL_TIME_ESTIMATE")}
                value={<HoverTip text={l.t("TIP_ETA")}>{formatEta()}</HoverTip>}
                color={theme.colors.textPrimary}
            />

            <StatRow
                label={l.t("INTEL_COMPOSITION")}
                value={<HoverTip text={l.t("TIP_COMPOSITION")}>{threatComposition ?? l.t("INTEL_UNKNOWN")}</HoverTip>}
                color={hasInsiderInfo ? accents.crisis.accent : theme.colors.textMuted}
                style={{ flexWrap: "wrap" }}
            />

            {(estimatedShaheds > 0 || estimatedBallistics > 0) && (
                <StatRow
                    label={l.t("INTEL_ESTIMATE")}
                    value={(
                        <>
                            {estimatedShaheds > 0 ? `${estimatedShaheds} ${l.t("INTEL_SHAHEDS")}` : ""}
                            {estimatedShaheds > 0 && estimatedBallistics > 0 ? " / " : ""}
                            {estimatedBallistics > 0 ? `${estimatedBallistics} ${l.t("INTEL_BALLISTICS")}` : ""}
                        </>
                    )}
                    color={hasInsiderInfo ? accents.crisis.accent : theme.colors.textMuted}
                />
            )}
        </div>
    );
});

AttackForecastSection.displayName = "AttackForecastSection";
