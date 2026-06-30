import React from "react";
import { Column, Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { IconAlert, IconBrain, IconChart, IconGlobe, IconShield, IconWifi, IconWifiOff } from "@shared/common/Icons";
import { ProgressBar, StatRow } from "../../../../shared/ui";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { styles } from "../CognitiveInfoSection.styles";

export const IPSOSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    const intensityColor = cw.IpsoIntensity >= 60 ? accents.crisis.accent
        : cw.IpsoIntensity >= 30 ? accents.resilience.accent
        : theme.colors.textMuted;

    const intensityLabel = cw.IpsoIntensity >= 60 ? l.t("IPSO_INTENSITY_HIGH")
        : cw.IpsoIntensity >= 30 ? l.t("IPSO_INTENSITY_MEDIUM")
        : l.t("IPSO_INTENSITY_LOW");

    return (
        <div style={styles.statusBox(theme)}>
            <Row justify="space-between" align="center">
                <Row align="center">
                    <span style={{ color: accents.crisis.accent, fontSize: "10rem", marginRight: "4rem" }}>
                        <IconAlert />
                    </span>
                    <span style={{ ...styles.miniLabel(theme), fontWeight: 700 }}>
                        {l.t("STATUS_ACTIVE")}
                    </span>
                </Row>
                <span style={{
                    fontSize: "12rem",
                    fontWeight: 700,
                    fontFamily: theme.typography.fontFamilyMono,
                    color: intensityColor,
                }}>
                    {cw.IpsoIntensity}% — {intensityLabel}
                </span>
            </Row>

            <div style={{ marginTop: theme.spacing.xs }}>
                <ProgressBar value={cw.IpsoIntensity} color={intensityColor} height="6rem" />
            </div>

            <StatRow
                compact
                label={l.t("IPSO_DISTRICTS_AFFECTED")}
                value={`${cw.IpsoDistrictCount} / ${cw.IpsoTotalDistricts}`}
                color={cw.IpsoDistrictCount > 0 ? intensityColor : theme.colors.textMuted}
                style={{ marginTop: theme.spacing.sm }}
                valueStyle={{ fontWeight: 600, fontSize: "12rem" }}
            />

            <div style={styles.divider(theme)} />

            <div style={{ marginBottom: theme.spacing.xs }}>
                <span style={{ ...styles.miniLabel(theme), fontWeight: 700 }}>
                    {l.t("IPSO_CHANNELS")}
                </span>
            </div>
            <Column gap="2rem">
                <Row align="center">
                    <span style={{ fontSize: "10rem", marginRight: "6rem" }}><IconWifi /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_CHANNEL_BOTS")}</span>
                </Row>
                <Row align="center">
                    <span style={{ fontSize: "10rem", marginRight: "6rem" }}><IconChart /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_CHANNEL_LEAFLETS")}</span>
                </Row>
                <Row align="center">
                    <span style={{ fontSize: "10rem", marginRight: "6rem" }}><IconGlobe /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_CHANNEL_TELEGRAM")}</span>
                </Row>
            </Column>

            <div style={styles.divider(theme)} />

            <div style={{ marginBottom: theme.spacing.xs }}>
                <span style={{ ...styles.miniLabel(theme), fontWeight: 700 }}>
                    {l.t("IPSO_COUNTERMEASURES")}
                </span>
            </div>
            <Column gap="2rem">
                <Row align="center">
                    <span style={{ fontSize: "10rem", color: theme.colors.success, marginRight: "6rem" }}><IconShield /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_COUNTER_HERO")}</span>
                </Row>
                <Row align="center">
                    <span style={{ fontSize: "10rem", color: accents.resilience.accent, marginRight: "6rem" }}><IconWifiOff /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_COUNTER_INTERNET")}</span>
                </Row>
                <Row align="center">
                    <span style={{ fontSize: "10rem", color: accents.schemes.accent, marginRight: "6rem" }}><IconBrain /></span>
                    <span style={styles.miniLabel(theme)}>{l.t("IPSO_COUNTER_TELEMARATHON")}</span>
                </Row>
            </Column>
        </div>
    );
};
