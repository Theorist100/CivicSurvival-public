/**
 * CognitiveInfoSection - Left column info panel
 * Shows global cognitive warfare stats, penalties breakdown, exodus, and Gerda status
 */

import React, { memo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { HelpSection } from "../../../shared/common/HelpSection";
import { Row } from "@coherent";
import { getPanelStyles, useTheme, useAccents } from "@themes";
import { useLocale } from "@locales";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { IconAlert } from "@shared/common/Icons";
import { DEFAULT_COGNITIVE_DTO } from "../../../../types/domainDtos";
import { ProgressBar, SectionHeader, SectionTitle, StatRow } from "../../../shared/ui";
import { styles } from "./CognitiveInfoSection.styles";
import { ExodusSection } from "./info/ExodusSection";
import { HouseholdStatsSection } from "./info/HouseholdStatsSection";
import { IPSOSection } from "./info/IPSOSection";
import { PenaltiesSection } from "./info/PenaltiesSection";
import { TheVoiceSection } from "./info/TheVoiceSection";

export const CognitiveInfoSection = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const avgIntegrity = cw.AvgIntegrity;

    const avgIntegrityPct = Math.round(avgIntegrity * 100);
    // dto-coverage-allow: compromised percentage is its own display ratio, not avgIntegrity recomputation.
    const compromisedPct = cw.TotalDistricts > 0
        ? Math.round((cw.CompromisedDistricts / cw.TotalDistricts) * 100)
        : 0;

    // Color based on average integrity
    const integrityColor = avgIntegrity >= 0.7 ? theme.colors.success
        : avgIntegrity >= 0.5 ? accents.resilience.accent
        : accents.crisis.accent;
    const sectionGap: React.CSSProperties = { marginTop: theme.spacing.md };
    const compactValueStyle: React.CSSProperties = { marginLeft: "4rem" };

    return (
        <div>
            {/* COGNITIVE WARFARE - Combined header with status */}
            <Row justify="space-between" align="center" style={{ marginBottom: theme.spacing.xs }}>
                <SectionHeader
                    title={l.t("UI_CW_TITLE")}
                    titleStyle={{ ...getPanelStyles(theme).sectionTitle(), marginBottom: 0 }}
                    help={<HelpSection id="cognitive" title={l.t("UI_CW_TITLE")}>{l.t("HELP_COGNITIVE")}</HelpSection>}
                />
                <span style={{
                    fontSize: "10rem",
                    fontWeight: 600,
                    color: cw.IpsoActive ? accents.crisis.accent : theme.colors.textMuted,
                    display: "flex",
                    alignItems: "center",
                }}>
                    {cw.IpsoActive ? <><span style={{ marginRight: "4rem" }}><IconAlert /></span>{l.t("STATUS_ACTIVE")}</> : l.t("STATUS_INACTIVE")}
                </span>
            </Row>
            <div style={styles.statusBox(theme)}>
                {/* Integrity row with inline progress bar */}
                <Row justify="space-between" align="center">
                    <span style={{
                        color: theme.colors.textMuted,
                        fontSize: theme.typography.sizeSM,
                        textTransform: "uppercase",
                    }}>
                        {l.t("UI_CW_INTEGRITY")}
                    </span>
                    <Row align="center" style={{ flex: 1, marginLeft: theme.spacing.sm, marginRight: theme.spacing.sm }}>
                        <ProgressBar value={avgIntegrityPct} color={integrityColor} height="6rem" style={{ flex: 1 }} />
                    </Row>
                    <HoverTip
                        text={l.t("TIP_CW_INTEGRITY")}
                        style={{
                            fontSize: "12rem",
                            fontWeight: 700,
                            fontFamily: theme.typography.fontFamilyMono,
                            color: integrityColor,
                            minWidth: "40rem",
                            textAlign: "right",
                        }}
                    >
                        {avgIntegrityPct}%
                    </HoverTip>
                </Row>

                {/* Districts + Compromised in one row */}
                <Row justify="space-between" align="center" style={{ marginTop: theme.spacing.xs }}>
                    <StatRow
                        compact
                        label={l.t("UI_CW_DISTRICTS")}
                        value={cw.TotalDistricts}
                        color={theme.colors.textPrimary}
                        style={{ flex: 1, paddingRight: theme.spacing.sm }}
                        valueStyle={compactValueStyle}
                    />
                    <StatRow
                        compact
                        label={l.t("UI_CW_COMPROMISED")}
                        value={<HoverTip text={l.t("TIP_CW_COMPROMISED")}>{cw.CompromisedDistricts} ({compromisedPct}%)</HoverTip>}
                        color={cw.CompromisedDistricts > 0 ? accents.crisis.accent : theme.colors.textMuted}
                        style={{ flex: 1 }}
                        valueStyle={compactValueStyle}
                    />
                </Row>
            </div>

            {/* ACTIVE PENALTIES */}
            <div style={sectionGap}>
                <SectionTitle>{l.t("UI_CW_PENALTIES")}</SectionTitle>
                <PenaltiesSection />
            </div>

            {/* EXODUS */}
            <div style={sectionGap}>
                <SectionTitle>{l.t("UI_CW_EXODUS")}</SectionTitle>
                <ExodusSection />
            </div>

            {/* HOUSEHOLD IMPACT (v2 PsyImpact stats) */}
            <div style={sectionGap}>
                <SectionTitle>{l.t("UI_CW_HOUSEHOLD_IMPACT")}</SectionTitle>
                <HouseholdStatsSection />
            </div>

            {/* ENEMY IPSO (propaganda) */}
            {cw.IpsoActive && (
                <div style={sectionGap}>
                    <SectionTitle>{l.t("IPSO_SECTION_TITLE")}</SectionTitle>
                    <IPSOSection />
                </div>
            )}

            {/* THE VOICE (Gerda) - Public Sentiment Indicator */}
            <div style={sectionGap}>
                <SectionTitle>{l.t("UI_CW_THE_VOICE")}</SectionTitle>
                <TheVoiceSection />
            </div>
        </div>
    );
});

CognitiveInfoSection.displayName = "CognitiveInfoSection";
