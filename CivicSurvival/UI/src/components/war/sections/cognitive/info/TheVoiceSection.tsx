import React, { useMemo } from "react";
import { HoverTip } from "../../../../shared/common/HoverTip";
import { Column, Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { HeroStatus, ProtestRisk, bindingDataOrDefault, getHeroStatusLabelKey, getProtestRiskLabelKey, useCognitive } from "@hooks/domain";
import { IconAlert, IconBrain } from "@shared/common/Icons";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { styles } from "../CognitiveInfoSection.styles";

export const TheVoiceSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const heroStatus = cw.HeroStatus;
    const protestRisk = cw.ProtestRisk;
    const isDeployed = heroStatus !== HeroStatus.Inactive;

    const portraitStyle = useMemo(() => {
        if (!isDeployed) return { WebkitFilter: "grayscale(100%)", filter: "grayscale(100%)", opacity: 0.5 };
        if (protestRisk === ProtestRisk.Critical) return { WebkitFilter: "hue-rotate(-30deg)", filter: "hue-rotate(-30deg)" };
        if (protestRisk === ProtestRisk.High) return { WebkitFilter: "hue-rotate(-15deg)", filter: "hue-rotate(-15deg)" };
        return {};
    }, [isDeployed, protestRisk]);

    const riskColor = useMemo(() => {
        switch (protestRisk) {
            case ProtestRisk.Low: return theme.colors.success;
            case ProtestRisk.Medium: return accents.resilience.accent;
            case ProtestRisk.High: return accents.crisis.accent;
            case ProtestRisk.Critical: return theme.colors.zoneRed;
            default: return theme.colors.textMuted;
        }
    }, [protestRisk, theme, accents]);

    const statusColor = useMemo(() => {
        switch (heroStatus) {
            case HeroStatus.Inactive: return theme.colors.textMuted;
            case HeroStatus.Deployed: return accents.schemes.accent;
            case HeroStatus.Lecturing: return theme.colors.success;
            default: return theme.colors.textMuted;
        }
    }, [heroStatus, theme, accents]);

    return (
        <div style={styles.voiceBox(theme, cw.ProtestRisk >= ProtestRisk.High)}>
            <Row align="center" style={{ marginBottom: theme.spacing.sm }}>
                <div style={{ ...styles.portrait(theme), ...portraitStyle }}>
                    <IconBrain />
                </div>
                <Column style={{ flex: 1 }}>
                    <Row justify="space-between" align="center">
                        <Column>
                            <span style={styles.heroName(theme)}>{l.t("UI_CW_GERDA")}</span>
                            <span style={{ fontSize: "10rem", color: theme.colors.textMuted, fontStyle: "italic" }}>
                                {l.t("UI_CW_GERDA_SUBTITLE")}
                            </span>
                        </Column>
                        <span style={styles.heroStatus(statusColor)}>
                            {l.t(getHeroStatusLabelKey(cw.HeroStatus))}
                        </span>
                    </Row>
                    {isDeployed && (
                        <span style={styles.heroLocation(theme)}>
                            {cw.HeroStatus === HeroStatus.Lecturing ? l.t("UI_CW_UNIVERSITY") : l.t("UI_CW_CITY_HALL")}
                        </span>
                    )}
                </Column>
            </Row>

            <div style={styles.narrativeBox(theme)}>
                <span style={styles.narrativeText(theme, cw.ProtestRisk)}>
                    {cw.DominantNarrative}
                </span>
            </div>

            <Row justify="space-between" align="center" style={{ marginTop: theme.spacing.sm }}>
                <Row align="center">
                    <span style={{ color: riskColor, fontSize: "14rem", marginRight: "6rem" }}>
                        <IconAlert />
                    </span>
                    <span style={styles.riskLabel(theme)}>{l.t("UI_CW_PROTEST_RISK")}</span>
                </Row>
                <HoverTip text={l.t("TIP_CW_PROTEST_RISK")} style={styles.riskValue(riskColor)}>
                    {l.t(getProtestRiskLabelKey(cw.ProtestRisk))}
                </HoverTip>
            </Row>
        </div>
    );
};
