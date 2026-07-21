/**
 * AttackForecastSection - Attack type, ETA, composition predictions
 * Intel domain → Right column
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { Row } from "@coherent";
import { StatRow, StatTile } from "../../../shared/ui";
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
    /** -1 (or 0 without insider) = the swarm is unread. */
    estimatedShaheds: number;
    estimatedBallistics: number;
    hasInsiderInfo: boolean;
}

export const AttackForecastSection: React.FC<AttackForecastSectionProps> = memo(({
    waveTypePrediction,
    isMassiveStrike,
    timeEstimate,
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

    // Counts arrive as -1 when the swarm is unread (no insider, or no wave scheduled yet).
    const swarmKnown = estimatedShaheds > 0 || estimatedBallistics > 0;
    const drones = estimatedShaheds > 0 ? `${estimatedShaheds} ${l.t("INTEL_SHAHEDS")}` : "";
    const ballistics = estimatedBallistics > 0 ? `${estimatedBallistics} ${l.t("INTEL_BALLISTICS")}` : "";
    const composition = swarmKnown
        ? [drones, ballistics].filter(Boolean).join(" / ")
        : l.t("INTEL_SWARM_UNKNOWN");

    return (
        <div style={s.section}>
            <div style={s.sectionTitleColored(accents.crisis.accent)}>
                {l.t("INTEL_ATTACK_FORECAST")}
            </div>

            {/* ETA and wave type are what this section is opened for — they lead as a pair of
                tiles, and a massive strike frames both of them red. Composition and the drone/
                ballistic estimate qualify that verdict, so they stay rows underneath. */}
            {/* Both values are phrases as often as figures ("Attack in progress", "Awaiting
                dawn/dusk"), so they run at label size — the big-number size is for the tiles
                that actually hold numbers. */}
            <Row gap={theme.spacing.xs} align="stretch" style={{ marginBottom: theme.spacing.xs }}>
                <StatTile
                    label={l.t("INTEL_TIME_ESTIMATE")}
                    value={<HoverTip text={l.t("TIP_ETA")}>{formatEta()}</HoverTip>}
                    valueSize={theme.typography.sizeMD}
                    color={isMassiveStrike ? accents.crisis.accent : theme.colors.textPrimary}
                    alertColor={isMassiveStrike ? accents.crisis.accent : undefined}
                />
                <StatTile
                    label={l.t("INTEL_WAVE_TYPE")}
                    // A locale id off the intel singleton, not display text — resolved here so the
                    // readout speaks the player's language.
                    value={(
                        <HoverTip text={l.t("TIP_WAVE_TYPE")}>
                            {waveTypePrediction ? l.tDynamic(waveTypePrediction) : l.t("INTEL_UNKNOWN")}
                        </HoverTip>
                    )}
                    valueSize={theme.typography.sizeMD}
                    color={isMassiveStrike ? accents.crisis.accent : theme.colors.textSecondary}
                    alertColor={isMassiveStrike ? accents.crisis.accent : undefined}
                />
            </Row>

            {/* One line, not two: the counts ARE the composition. The backend used to also ship a
                pre-phrased "Est. 24 Shaheds, 6 Cruise Missiles" — the same numbers a second time,
                and in English whatever the player's language. */}
            <StatRow
                label={l.t("INTEL_COMPOSITION")}
                value={<HoverTip text={l.t("TIP_COMPOSITION")}>{composition}</HoverTip>}
                color={swarmKnown ? accents.crisis.accent : theme.colors.textMuted}
                style={{ flexWrap: "wrap" }}
            />
        </div>
    );
});

AttackForecastSection.displayName = "AttackForecastSection";
