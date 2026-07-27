/**
 * InfoWarTab - Telemarathon controls
 * Zone 3, Tab 1 of Cognitive Warfare Sandwich
 */

import React, { memo, useCallback, useMemo, useRef } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../../themes";
import { bindingDataOrDefault, isBindingLive, useCognitive, NarrativeMode, type NarrativeModeType } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { IconAlert, IconShield, IconChart, IconNews } from "../../../../shared/common/Icons";
import { HelpSection } from "../../../../shared/common/HelpSection";
import { ProgressBar } from "../../../../shared/ui";
import { useLocale } from "../../../../../locales";
import { useOptimisticChoice, useRequestAction, type useCognitiveActions } from "@hooks/actions";

interface InfoWarTabProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const InfoWarTab = memo(({ actions }: InfoWarTabProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const cwState = useCognitive();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    // Default MediaTrust=0 would paint a red 0% trust reading before the
    // cognitive feed delivers — keep the trust row muted until it is live.
    const live = isBindingLive(cwState);
    const telemarathonActive = cw?.TelemarathonActive ?? false;
    const narrativeMode = (cw?.NarrativeMode ?? NarrativeMode.Realistic) as NarrativeModeType;
    const mediaTrust = cw?.MediaTrust ?? 0;
    const isInShock = cw?.IsInShock ?? false;
    const shockHoursRemaining = cw?.ShockHoursRemaining ?? 0;
    const audienceFatigue = cw?.AudienceFatigue ?? 0;
    const telemarathonRef = useRef(false);
    const telemarathonAction = useRequestAction(() => {
        actions.setTelemarathonActive(telemarathonRef.current);
        return true;
    }, cw?.TelemarathonActiveRequest);
    const telemarathonPending = telemarathonAction.isPending;
    const telemarathonError = telemarathonAction.failureReasonId;

    const handleToggle = useCallback(() => {
        telemarathonRef.current = !telemarathonActive;
        telemarathonAction.execute();
    }, [telemarathonActive, telemarathonAction]);

    // Narrative tone through the shared optimistic-choice mechanism: the picked
    // mode highlights immediately and reconciles against the backend (a reject
    // rolls the highlight back on the next DTO snapshot).
    const modeChoice = useOptimisticChoice<NarrativeModeType>(
        narrativeMode,
        (m) => actions.setNarrativeMode(m),
        cw?.TelemarathonModeRequest
    );

    const getModeInfo = (mode: number): { desc: string } => {
        switch (mode) {
            case NarrativeMode.Soothing:
                return { desc: l.t("UI_IW_MODE_SOOTHING_DESC") };
            case NarrativeMode.Alarmist:
                return { desc: l.t("UI_IW_MODE_ALARMIST_DESC") };
            case NarrativeMode.Realistic:
            default:
                return { desc: l.t("UI_IW_MODE_REALISTIC_DESC") };
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
        } as React.CSSProperties,
    }), [theme, accents]);

    const trustColor = !live ? theme.colors.textMuted
        : mediaTrust >= 0.5 ? theme.colors.success : accents.crisis.accent;
    const fatigueColor = audienceFatigue > 0.5 ? accents.crisis.accent : accents.resilience.accent;

    return (
        <Column style={s.container}>
            {/* Broadcast Toggle — the "?" explains the broadcast war: trust as the resource that
                makes airtime work, the tone trade, audience fatigue, and the hard limit (a broadcast
                never blunts a landed raid; only the speaker archetype and ground aid do). */}
            <Row justify="space-between" align="center" style={s.controlRow}>
                <Row align="center" gap={theme.spacing.xs}>
                    {/* Icon wrapped in a DOM span: Row's gap arrives via cloneElement(style),
                        and cs2/ui Icon drops the style prop (see UI_COHERENT_BEST_PRACTICES,
                        "Row/Column gap — це cloneElement"). */}
                    <span style={{ display: "flex" }}>
                        <IconNews />
                    </span>
                    <span style={s.controlLabel}>{l.t("UI_IW_TELEMARATHON")}</span>
                    <HelpSection id="infowar" title={l.t("UI_IW_TELEMARATHON")}>{l.t("HELP_INFOWAR")}</HelpSection>
                </Row>
                <button
                    style={s.toggleButton(telemarathonActive, telemarathonPending)}
                    disabled={telemarathonPending}
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
                            style={s.modeButton(modeChoice.shown === NarrativeMode.Soothing, theme.colors.success)}
                            disabled={modeChoice.isPending}
                            onClick={() => modeChoice.pick(NarrativeMode.Soothing)}
                        >
                            <span style={s.modeIcon}><IconShield /></span> {l.t("UI_IW_SOOTHING")}
                        </button>
                        <button
                            style={s.modeButton(modeChoice.shown === NarrativeMode.Alarmist, accents.crisis.accent)}
                            disabled={modeChoice.isPending}
                            onClick={() => modeChoice.pick(NarrativeMode.Alarmist)}
                        >
                            <span style={s.modeIcon}><IconAlert /></span> {l.t("UI_IW_ALARMIST")}
                        </button>
                        <button
                            style={s.modeButton(modeChoice.shown === NarrativeMode.Realistic, accents.resilience.accent)}
                            disabled={modeChoice.isPending}
                            onClick={() => modeChoice.pick(NarrativeMode.Realistic)}
                        >
                            <span style={s.modeIcon}><IconChart /></span> {l.t("UI_IW_REALISTIC")}
                        </button>
                    </Row>
                    <div style={s.hint}>{modeChoice.failureReasonId ? l.tDynamic(modeChoice.failureReasonId) : getModeInfo(modeChoice.shown).desc}</div>
                </>
            )}

            {/* Trust & Fatigue */}
            <div style={s.trustSection}>
                <Row justify="space-between" align="center">
                    <span style={s.trustLabel}>{l.t("UI_IW_MEDIA_TRUST")}</span>
                    <span style={s.trustValue(trustColor)}>{live ? `${Math.round(mediaTrust * 100)}%` : "—"}</span>
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

InfoWarTab.displayName = "InfoWarTab";
