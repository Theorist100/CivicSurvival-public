/**
 * InfoWarTab - Telemarathon controls
 * Zone 3, Tab 1 of Cognitive Warfare Sandwich
 */

import React, { memo, useCallback, useMemo, useRef } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { bindingDataOrDefault, useCognitive, NarrativeMode, type NarrativeModeType } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { IconAlert, IconShield, IconChart, IconNews } from "../../../../shared/common/Icons";
import { ProgressBar } from "../../../../shared/ui";
import { useLocale } from "../../../../../locales";
import { useRequestAction } from "@hooks/actions";
import { type useCognitiveActions } from "@hooks/actions";

interface InfoWarTabProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

interface InfoWarTabContentProps extends InfoWarTabProps {
    narrativeOpen: boolean;
}

const InfoWarTabContent = memo(({ actions, narrativeOpen }: InfoWarTabContentProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const telemarathonActive = cw?.TelemarathonActive ?? false;
    const narrativeMode = cw?.NarrativeMode ?? NarrativeMode.Realistic;
    const mediaTrust = cw?.MediaTrust ?? 0;
    const isInShock = cw?.IsInShock ?? false;
    const shockHoursRemaining = cw?.ShockHoursRemaining ?? 0;
    const audienceFatigue = cw?.AudienceFatigue ?? 0;
    const telemarathonRef = useRef(false);
    const telemarathonAction = useRequestAction(() => {
        if (!narrativeOpen) return false;
        actions.setTelemarathonActive(telemarathonRef.current);
        return true;
    }, cw?.TelemarathonActiveRequest);
    const telemarathonPending = telemarathonAction.isPending || cw?.TelemarathonActiveRequest.Status === "pending";
    const telemarathonError =
        cw?.TelemarathonActiveRequest.Status === "failed" && cw.TelemarathonActiveRequest.ReasonId
            ? cw.TelemarathonActiveRequest.ReasonId
            : "";

    const handleToggle = useCallback(() => {
        telemarathonRef.current = !telemarathonActive;
        telemarathonAction.execute();
    }, [telemarathonActive, telemarathonAction]);

    const handleSetMode = useCallback((mode: NarrativeModeType) => {
        if (!narrativeOpen) return;
        actions.setNarrativeMode(mode);
    }, [actions, narrativeOpen]);
    const modeRequest = cw?.TelemarathonModeRequest;
    const modePending = modeRequest?.Status === "pending";

    const getModeInfo = (mode: number) => {
        switch (mode) {
            case NarrativeMode.Soothing:
                return { name: "SOOTHING", color: theme.colors.success, desc: l.t("UI_IW_MODE_SOOTHING_DESC") };
            case NarrativeMode.Alarmist:
                return { name: "ALARMIST", color: accents.crisis.accent, desc: l.t("UI_IW_MODE_ALARMIST_DESC") };
            case NarrativeMode.Realistic:
            default:
                return { name: "REALISTIC", color: accents.resilience.accent, desc: l.t("UI_IW_MODE_REALISTIC_DESC") };
        }
    };

    const s = useMemo(() => ({
        container: {
            padding: theme.spacing.sm,
            height: "100%",
        } as React.CSSProperties,

        controlRow: {
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,

        controlLabel: {
            fontSize: "11rem",
            fontWeight: 600,
            color: theme.colors.textSecondary,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,

        toggleButton: (isOn: boolean, isPending: boolean) => ({
            padding: "6rem 16rem",
            fontSize: "11rem",
            fontWeight: 700,
            border: "none",
            borderRadius: "4rem",
            cursor: isPending ? "not-allowed" : "pointer",
            backgroundColor: isOn ? theme.colors.success : theme.colors.surface,
            color: isOn ? theme.colors.white : theme.colors.textMuted,
            opacity: isPending ? 0.65 : 1,
        }) as React.CSSProperties,

        modeButton: (isActive: boolean, color: string) => ({
            flex: 1,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "8rem",
            fontSize: "11rem",
            fontWeight: 600,
            border: isActive ? `3rem solid ${color}` : `2rem solid ${theme.colors.border}`,
            borderRadius: "4rem",
            backgroundColor: isActive ? hexToRgba(color, 0.12) : "transparent",
            color: isActive ? color : theme.colors.textMuted,
            cursor: "pointer",
        }) as React.CSSProperties,

        modeIcon: {
            marginRight: "4rem",
        } as React.CSSProperties,

        trustSection: {
            marginTop: theme.spacing.sm,
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.surface,
            borderRadius: "4rem",
        } as React.CSSProperties,

        trustLabel: {
            fontSize: "10rem",
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,

        trustValue: (color: string) => ({
            fontSize: "18rem",
            fontWeight: 700,
            color,
            fontFamily: theme.typography.fontFamilyMono,
        }) as React.CSSProperties,

        shockWarning: {
            marginTop: theme.spacing.xs,
            padding: "4rem 8rem",
            backgroundColor: hexToRgba(accents.crisis.accent, 0.12),
            color: accents.crisis.accent,
            fontSize: "11rem",
            fontWeight: 600,
            borderRadius: "4rem",
            display: "flex",
            alignItems: "center",
        } as React.CSSProperties,

        shockIcon: {
            marginRight: "4rem",
        } as React.CSSProperties,

        hint: {
            marginTop: theme.spacing.xs,
            fontSize: "10rem",
            color: theme.colors.textMuted,
            fontStyle: "italic" as const,
        } as React.CSSProperties,
    }), [theme, accents]);

    const trustColor = mediaTrust >= 0.5 ? theme.colors.success : accents.crisis.accent;
    const fatigueColor = audienceFatigue > 0.5 ? accents.crisis.accent : accents.resilience.accent;

    return (
        <Column style={s.container}>
            {/* Broadcast Toggle */}
            <Row justify="space-between" align="center" style={s.controlRow}>
                <Row align="center" gap={theme.spacing.xs}>
                    <IconNews />
                    <span style={s.controlLabel}>{l.t("UI_IW_TELEMARATHON")}</span>
                </Row>
                <button
                    style={s.toggleButton(telemarathonActive, telemarathonPending)}
                    disabled={!narrativeOpen || telemarathonPending}
                    onClick={handleToggle}
                >
                    {telemarathonPending
                        ? l.t("UI_PROCESSING")
                        : telemarathonActive
                        ? l.t("UI_IW_ON_AIR")
                        : l.t("UI_OFF")}
                </button>
            </Row>
            {telemarathonError && <div style={s.hint}>{l.tDynamic(telemarathonError)}</div>}

            {/* Shock Warning */}
            {isInShock && (
                <div style={s.shockWarning}>
                    <span style={s.shockIcon}><IconAlert /></span> {l.t("UI_IW_SHOCK_WARNING", shockHoursRemaining.toFixed(1))}
                </div>
            )}

            {/* Narrative Mode Selector */}
            {telemarathonActive && (
                <>
                    <div style={{ ...s.controlLabel, marginBottom: theme.spacing.xs }}>{l.t("UI_IW_NARRATIVE_TONE")}</div>
                    <Row gap={theme.spacing.xs} style={s.controlRow}>
                        <button
                            style={s.modeButton(narrativeMode === NarrativeMode.Soothing, theme.colors.success)}
                            disabled={!narrativeOpen || modePending}
                            onClick={() => handleSetMode(NarrativeMode.Soothing)}
                        >
                            <span style={s.modeIcon}><IconShield /></span> {l.t("UI_IW_SOOTHING")}
                        </button>
                        <button
                            style={s.modeButton(narrativeMode === NarrativeMode.Alarmist, accents.crisis.accent)}
                            disabled={!narrativeOpen || modePending}
                            onClick={() => handleSetMode(NarrativeMode.Alarmist)}
                        >
                            <span style={s.modeIcon}><IconAlert /></span> {l.t("UI_IW_ALARMIST")}
                        </button>
                        <button
                            style={s.modeButton(narrativeMode === NarrativeMode.Realistic, accents.resilience.accent)}
                            disabled={!narrativeOpen || modePending}
                            onClick={() => handleSetMode(NarrativeMode.Realistic)}
                        >
                            <span style={s.modeIcon}><IconChart /></span> {l.t("UI_IW_REALISTIC")}
                        </button>
                    </Row>
                    <div style={s.hint}>{getModeInfo(narrativeMode).desc}</div>
                </>
            )}

            {/* Trust & Fatigue */}
            <div style={s.trustSection}>
                <Row justify="space-between" align="center">
                    <span style={s.trustLabel}>{l.t("UI_IW_MEDIA_TRUST")}</span>
                    <span style={s.trustValue(trustColor)}>{Math.round(mediaTrust * 100)}%</span>
                </Row>
                <Row align="center" style={{ marginTop: "4rem" }}>
                    <ProgressBar value={mediaTrust * 100} color={trustColor} height="8rem" style={{ flex: 1, marginLeft: theme.spacing.sm, marginRight: theme.spacing.sm }} />
                </Row>

                {telemarathonActive && audienceFatigue > 0.1 && (
                    <Row justify="space-between" align="center" style={{ marginTop: theme.spacing.xs }}>
                        <span style={s.trustLabel}>{l.t("UI_IW_FATIGUE")}</span>
                        <Row align="center" style={{ flex: 1 }}>
                            <ProgressBar value={audienceFatigue * 100} color={fatigueColor} height="8rem" style={{ flex: 1, marginLeft: theme.spacing.sm, marginRight: theme.spacing.sm }} />
                            <span style={{ fontSize: "11rem", color: fatigueColor, fontWeight: 600 }}>
                                {Math.round(audienceFatigue * 100)}%
                            </span>
                        </Row>
                    </Row>
                )}
            </div>
        </Column>
    );
});

InfoWarTabContent.displayName = "InfoWarTabContent";

export const InfoWarTab = memo(({ actions }: InfoWarTabProps) => (
    <InfoWarTabContent actions={actions} narrativeOpen />
));

InfoWarTab.displayName = "InfoWarTab";
