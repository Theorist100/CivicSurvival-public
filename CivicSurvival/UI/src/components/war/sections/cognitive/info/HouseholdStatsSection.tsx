import React from "react";
import { HoverTip } from "../../../../shared/common/HoverTip";
import { Column, Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { type CognitiveDto, bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { IconAlert, IconBrain, IconEye, IconLightning } from "@shared/common/Icons";
import { ProgressBar, StatRow } from "../../../../shared/ui";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { styles } from "../CognitiveInfoSection.styles";

export const HouseholdStatsSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    const blackoutPct = cw.TotalHouseholds > 0 ? Math.round((cw.HouseholdsUnderBlackout / cw.TotalHouseholds) * 100) : 0;
    const envyPct = cw.TotalHouseholds > 0 ? Math.round((cw.HouseholdsWithEnvy / cw.TotalHouseholds) * 100) : 0;
    const impactPct = cw.TotalHouseholds > 0 ? Math.round((cw.HouseholdsUnderImpact / cw.TotalHouseholds) * 100) : 0;
    const infectedPct = cw.TotalHouseholds > 0 ? Math.round((cw.HouseholdsInfected / cw.TotalHouseholds) * 100) : 0;

    const getBarColorPositive = (value: number) => {
        if (value >= 0.7) return theme.colors.success;
        if (value >= 0.4) return accents.resilience.accent;
        return accents.crisis.accent;
    };

    const getBarColorNegative = (value: number) => {
        if (value >= 0.7) return accents.crisis.accent;
        if (value >= 0.4) return accents.resilience.accent;
        return theme.colors.success;
    };
    const compactMetricValue = (color: string): React.CSSProperties => ({
        fontSize: "10rem",
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color,
        minWidth: "32rem",
        textAlign: "right",
    });

    if (cw.TotalHouseholds === 0) {
        return (
            <div style={styles.statusBox(theme)}>
                <div style={styles.emptyState(theme)}>{l.t("UI_CW_NO_HOUSEHOLDS")}</div>
            </div>
        );
    }

    return (
        <div style={styles.statusBox(theme)}>
            <StatRow
                label={l.t("UI_CW_HOUSEHOLDS")}
                value={cw.TotalHouseholds.toLocaleString()}
                color={theme.colors.textPrimary}
                emphasis="title"
                style={{ marginBottom: theme.spacing.sm }}
            />

            <Column gap={theme.spacing.xs}>
                <Row justify="space-between" align="center">
                    <HoverTip text={l.t("TIP_CW_INFECTION")} style={styles.miniLabel(theme)}>
                        {l.t("UI_CW_AVG_INFECTION")}
                    </HoverTip>
                    <Row align="center" style={{ flex: 1, marginLeft: theme.spacing.sm }}>
                        <ProgressBar
                            value={Math.round(cw.AvgInfection * 100)}
                            color={getBarColorNegative(cw.AvgInfection)}
                            height="6rem"
                            style={{ flex: 1, marginRight: "6rem" }}
                        />
                        <span style={compactMetricValue(getBarColorNegative(cw.AvgInfection))}>
                            {Math.round(cw.AvgInfection * 100)}%
                        </span>
                    </Row>
                </Row>

                <Row justify="space-between" align="center">
                    <HoverTip text={l.t("TIP_CW_RESISTANCE")} style={styles.miniLabel(theme)}>
                        {l.t("UI_CW_AVG_RESISTANCE")}
                    </HoverTip>
                    <Row align="center" style={{ flex: 1, marginLeft: theme.spacing.sm }}>
                        <ProgressBar
                            value={Math.round(cw.AvgResistance * 100)}
                            color={getBarColorPositive(cw.AvgResistance)}
                            height="6rem"
                            style={{ flex: 1, marginRight: "6rem" }}
                        />
                        <span style={compactMetricValue(getBarColorPositive(cw.AvgResistance))}>
                            {Math.round(cw.AvgResistance * 100)}%
                        </span>
                    </Row>
                </Row>

                <Row justify="space-between" align="center">
                    <HoverTip text={l.t("TIP_CW_TRAUMA")} style={styles.miniLabel(theme)}>
                        {l.t("UI_CW_AVG_TRAUMA")}
                    </HoverTip>
                    <Row align="center" style={{ flex: 1, marginLeft: theme.spacing.sm }}>
                        <ProgressBar
                            value={Math.round(cw.AvgTrauma * 100)}
                            color={getBarColorNegative(cw.AvgTrauma)}
                            height="6rem"
                            style={{ flex: 1, marginRight: "6rem" }}
                        />
                        <span style={compactMetricValue(getBarColorNegative(cw.AvgTrauma))}>
                            {Math.round(cw.AvgTrauma * 100)}%
                        </span>
                    </Row>
                </Row>
            </Column>

            <div style={styles.divider(theme)} />

            <Column gap={theme.spacing.xs}>
                <StatRow
                    compact
                    label={<Row align="center"><span style={styles.stressIcon(accents.crisis.accent)}><IconLightning /></span>{l.t("UI_CW_BLACKOUT_STAT")}</Row>}
                    value={`${cw.HouseholdsUnderBlackout.toLocaleString()} (${blackoutPct}%)`}
                    color={cw.HouseholdsUnderBlackout > 0 ? accents.crisis.accent : theme.colors.textMuted}
                    labelStyle={{ minWidth: 0 }}
                    valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
                />

                <StatRow
                    compact
                    label={<Row align="center"><span style={styles.stressIcon(accents.resilience.accent)}><IconEye /></span>{l.t("UI_CW_ENVY_STAT")}</Row>}
                    value={`${cw.HouseholdsWithEnvy.toLocaleString()} (${envyPct}%)`}
                    color={cw.HouseholdsWithEnvy > 0 ? accents.resilience.accent : theme.colors.textMuted}
                    labelStyle={{ minWidth: 0 }}
                    valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
                />

                <StatRow
                    compact
                    label={<Row align="center"><span style={styles.stressIcon(accents.crisis.accent)}><IconBrain /></span>{l.t("UI_CW_DANGER_STAT")}</Row>}
                    value={`${cw.HouseholdsUnderImpact.toLocaleString()} (${impactPct}%)`}
                    color={cw.HouseholdsUnderImpact > 0 ? accents.crisis.accent : theme.colors.textMuted}
                    labelStyle={{ minWidth: 0 }}
                    valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
                />

                <StatRow
                    compact
                    label={<Row align="center"><span style={styles.stressIcon(theme.colors.zoneRed)}><IconAlert /></span>{l.t("UI_CW_INFECTED_STAT")}</Row>}
                    value={`${cw.HouseholdsInfected.toLocaleString()} (${infectedPct}%)`}
                    color={cw.HouseholdsInfected > 0 ? theme.colors.zoneRed : theme.colors.textMuted}
                    labelStyle={{ minWidth: 0 }}
                    valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
                />
            </Column>

            {cw.VulnerableHouseholds > 0 && (
                <>
                    <div style={styles.divider(theme)} />
                    <BlackoutVulnerabilityIndicator cw={cw} />
                </>
            )}
        </div>
    );
};

interface VulnerabilityProps {
    cw: CognitiveDto;
}

const BlackoutVulnerabilityIndicator: React.FC<VulnerabilityProps> = ({ cw }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const vulnerablePct = cw.TotalHouseholds > 0
        ? Math.round((cw.VulnerableHouseholds / cw.TotalHouseholds) * 100)
        : 0;

    const vulnerabilityBonus = Math.round((cw.BlackoutVulnerability - 1) * 100);

    const vulnColor = vulnerabilityBonus >= 20 ? theme.colors.zoneRed
        : vulnerabilityBonus >= 10 ? accents.crisis.accent
        : accents.resilience.accent;

    return (
        <Column gap={theme.spacing.xs}>
            <Row justify="space-between" align="center">
                <Row align="center">
                    <span style={{
                        fontSize: "10rem",
                        color: vulnColor,
                        marginRight: "4rem",
                    }}>
                        <IconBrain />
                    </span>
                    <span style={{
                        ...styles.miniLabel(theme),
                        fontWeight: 700,
                    }}>
                        {l.t("UI_CW_VULNERABLE")}
                    </span>
                </Row>
                <span style={{
                    fontSize: "11rem",
                    fontWeight: 700,
                    fontFamily: theme.typography.fontFamilyMono,
                    color: vulnColor,
                }}>
                    {vulnerabilityBonus > 0 ? l.t("UI_CW_PROPAGANDA_BONUS", vulnerabilityBonus) : "—"}
                </span>
            </Row>

            <StatRow
                compact
                label={l.t("UI_CW_VULNERABLE_HOUSEHOLDS")}
                value={`${cw.VulnerableHouseholds.toLocaleString()} (${vulnerablePct}%)`}
                color={vulnColor}
                valueStyle={{ fontSize: "11rem", fontWeight: 600 }}
            />

            <StatRow
                compact
                label={l.t("UI_CW_AVG_BLACKOUT_DURATION")}
                value={`${cw.AvgBlackoutHours.toFixed(1)}h`}
                color={vulnColor}
                valueStyle={{ fontSize: "11rem", fontWeight: 400 }}
            />
        </Column>
    );
};
