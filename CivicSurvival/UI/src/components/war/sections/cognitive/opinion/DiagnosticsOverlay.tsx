/**
 * DiagnosticsOverlay — the INFO accordion, slid in over the board. Reuses the
 * existing diagnostic sections verbatim (penalties / exodus / household stats /
 * IPSO) plus the dominant narrative + protest risk re-homed from the old
 * TheVoiceSection. Gerda's deploy status is NOT here — it lives in the hero
 * picker (archetype Voice). Fixed inside the panel, closes with ✕.
 */

import React, { memo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, Z_INDEX } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import {
    bindingDataOrDefault, isBindingLive, useCognitive, ProtestRisk, getProtestRiskLabelKey,
} from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { SectionTitle } from "../../../../shared/ui";
import { IconAlert } from "../../../../shared/common/Icons";
import { PenaltiesSection } from "../info/PenaltiesSection";
import { ExodusSection } from "../info/ExodusSection";
import { HouseholdStatsSection } from "../info/HouseholdStatsSection";
import { IPSOSection } from "../info/IPSOSection";

interface DiagnosticsOverlayProps {
    onClose: () => void;
}

export const DiagnosticsOverlay = memo(({ onClose }: DiagnosticsOverlayProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const cwState = useCognitive();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    // Default ProtestRisk 0 would paint a green LOW verdict before the feed
    // delivers — keep the risk read muted until the data is live.
    const live = isBindingLive(cwState);

    const riskColor = !live ? theme.colors.textMuted
        : cw.ProtestRisk >= ProtestRisk.High ? theme.colors.error
            : cw.ProtestRisk >= ProtestRisk.Medium ? accents.resilience.accent
                : theme.colors.success;

    const sectionGap: React.CSSProperties = { marginTop: theme.spacing.md };

    return (
        <div style={{
            position: "absolute",
            top: 0, left: 0, right: 0, bottom: 0,
            zIndex: Z_INDEX.dropdown,
            backgroundColor: theme.colors.paper,
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadiusLg,
            display: "flex",
            flexDirection: "column",
            minHeight: "100rem",
        }}>
            <Row align="center" justify="space-between" style={{
                padding: `${theme.spacing.sm} ${theme.spacing.md}`,
                borderBottom: `2rem solid ${theme.colors.border}`,
                backgroundColor: theme.colors.borderLight,
            }}>
                <span style={{
                    fontSize: theme.typography.sizeSM, fontWeight: 700, fontFamily: theme.typography.fontFamilyMono,
                    color: theme.colors.textPrimary, textTransform: "uppercase", letterSpacing: "0.6rem",
                }}>{l.t("UI_OB_DIAGNOSTICS")}</span>
                <button
                    onClick={onClose}
                    style={{
                        display: "flex", alignItems: "center",
                        padding: "4rem 10rem", backgroundColor: "transparent",
                        border: `2rem solid ${theme.colors.border}`, borderRadius: theme.layout.borderRadius,
                        color: theme.colors.textSecondary, cursor: "pointer",
                        fontSize: theme.typography.sizeXS, fontWeight: 700, fontFamily: theme.typography.fontFamilyMono,
                        textTransform: "uppercase", letterSpacing: "0.4rem",
                    }}
                >
                    {l.t("UI_OB_CLOSE")}
                </button>
            </Row>

            <Column style={{ flex: 1, overflowY: "auto", overflowX: "hidden", padding: theme.spacing.md }}>
                {/* WHAT YOU RISK */}
                <div>
                    <SectionTitle>{l.t("UI_OB_DIAG_RISK")}</SectionTitle>
                    <div style={sectionGap}>
                        <SectionTitle>{l.t("UI_CW_PENALTIES")}</SectionTitle>
                        <PenaltiesSection />
                    </div>
                    <div style={sectionGap}>
                        <SectionTitle>{l.t("UI_CW_EXODUS")}</SectionTitle>
                        <ExodusSection />
                    </div>
                </div>

                {/* HOUSEHOLD IMPACT */}
                <div style={sectionGap}>
                    <SectionTitle>{l.t("UI_CW_HOUSEHOLD_IMPACT")}</SectionTitle>
                    <HouseholdStatsSection />
                </div>

                {/* ENEMY IPSO */}
                {cw.IpsoActive && (
                    <div style={sectionGap}>
                        <SectionTitle>{l.t("IPSO_SECTION_TITLE")}</SectionTitle>
                        <IPSOSection />
                    </div>
                )}

                {/* NARRATIVE + PROTEST (re-homed from TheVoiceSection) */}
                <div style={sectionGap}>
                    <SectionTitle>{l.t("UI_OB_DIAG_NARRATIVE")}</SectionTitle>
                    <div style={{
                        padding: theme.spacing.sm,
                        backgroundColor: theme.colors.surface,
                        border: `2rem solid ${theme.colors.border}`,
                        borderRadius: theme.layout.borderRadiusLg,
                    }}>
                        <span style={{
                            fontSize: theme.typography.sizeSM, color: theme.colors.textPrimary,
                            borderLeft: `4rem solid ${theme.colors.textMuted}`, paddingLeft: theme.spacing.sm, lineHeight: 1.4,
                            display: "block",
                        }}>
                            {cw.DominantNarrative}
                        </span>
                        <Row align="center" justify="space-between" style={{ marginTop: theme.spacing.sm }}>
                            <Row align="center">
                                <span style={{ color: riskColor, marginRight: "5rem", display: "flex" }}><IconAlert /></span>
                                <span style={{
                                    fontSize: theme.typography.sizeXS, color: theme.colors.textMuted,
                                    textTransform: "uppercase", letterSpacing: "0.4rem",
                                }}>{l.t("UI_CW_PROTEST_RISK")}</span>
                            </Row>
                            <span style={{
                                fontSize: theme.typography.sizeSM, fontWeight: 700, color: riskColor,
                                textTransform: "uppercase", letterSpacing: "0.4rem",
                            }}>{live ? l.t(getProtestRiskLabelKey(cw.ProtestRisk)) : "—"}</span>
                        </Row>
                    </div>
                </div>
            </Column>
        </div>
    );
});

DiagnosticsOverlay.displayName = "DiagnosticsOverlay";
