/**
 * GlobalNetSection - Zone 1 of Cognitive Warfare right panel
 * Global internet mode selector: OPEN / FIREWALL / BLACKOUT
 */

import React, { memo, useMemo } from "react";
import { Row, Column } from "../../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../../themes";
import { useLocale } from "../../../../locales";
import { bindingDataOrDefault, useCognitive, InternetMode, type InternetModeType } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../types/domainDtos";
import { IconGlobe, IconShield, IconLightning } from "../../../shared/common/Icons";
import { useOptimisticChoice, type useCognitiveActions } from "@hooks/actions";

interface ModeButtonProps {
    mode: number;
    currentMode: number;
    label: string;
    icon: React.ReactNode;
    color: string;
    onClick: () => void;
    disabled?: boolean;
}

const ModeButton: React.FC<ModeButtonProps> = memo(({
    mode,
    currentMode,
    label,
    icon,
    color,
    onClick,
    disabled = false,
}) => {
    const theme = useTheme();
    const isActive = mode === currentMode;

    const style: React.CSSProperties = {
        flex: 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "8rem 12rem",
        fontSize: "11rem",
        fontWeight: 700,
        textTransform: "uppercase",
        border: isActive ? `3rem solid ${color}` : `2rem solid ${theme.colors.border}`,
        borderRadius: "4rem",
        backgroundColor: isActive ? hexToRgba(color, 0.12) : "transparent",
        color: isActive ? color : theme.colors.textMuted,
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.55 : 1,
        transition: "color 0.15s ease, background-color 0.15s ease, border-color 0.15s ease",
    };

    const iconStyle: React.CSSProperties = {
        marginRight: "6rem",
    };

    return (
        <button style={style} disabled={disabled} onClick={onClick}>
            <span style={iconStyle}>{icon}</span>
            {label}
        </button>
    );
});

ModeButton.displayName = "ModeButton";

interface GlobalNetSectionProps {
    actions: ReturnType<typeof useCognitiveActions>;
    disabled?: boolean;
}

export const GlobalNetSection = memo(({ actions, disabled = false }: GlobalNetSectionProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const currentMode = (cw?.InternetMode ?? InternetMode.Open) as InternetModeType;
    const infectionRate = cw?.InfectionRate ?? 0;
    const recoveryRate = cw?.RecoveryRate ?? 0;
    const penalty = cw?.CommercePenalty ?? 0;
    const modeChoice = useOptimisticChoice<InternetModeType>(
        currentMode,
        (m) => actions.setInternetMode(m),
        cw?.InternetModeRequest
    );
    const internetModePending = modeChoice.isPending;
    const internetModeError = modeChoice.failureReasonId;

    const s = useMemo(() => ({
        container: {
            width: "100%",
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.paper,
            borderBottom: `2rem solid ${theme.colors.border}`,
        } as React.CSSProperties,

        buttonsRow: {
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,

        // Infection / recovery / commerce — three equal columns in ONE row (label
        // over value), so the card stays low and the rail fits without scrolling.
        statLabel: {
            fontSize: "12rem",
            color: theme.colors.textMuted,
        } as React.CSSProperties,
        statValue: {
            fontSize: "14rem",
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
        } as React.CSSProperties,

        requestHint: {
            marginTop: theme.spacing.xs,
            fontSize: "10rem",
            color: theme.colors.textMuted,
            textAlign: "center" as const,
        } as React.CSSProperties,
    }), [theme]);

    // Format rate as percentage per hour
    const formatRate = (rate: number) => {
        const pctPerHour = rate * 100;
        if (pctPerHour === 0) return "0%";
        return `${pctPerHour > 0 ? "+" : ""}${pctPerHour.toFixed(1)}%${l.t("UI_UNIT_PER_HR")}`;
    };

    // Determine colors based on values
    const infectionColor = infectionRate > 0 ? accents.crisis.accent : theme.colors.textMuted;
    const recoveryColor = recoveryRate > 0 ? theme.colors.success : theme.colors.textMuted;
    const penaltyColor = penalty > 0 ? accents.resilience.accent : theme.colors.textMuted;
    const pickMode = (mode: InternetModeType): void => {
        if (disabled) return;
        modeChoice.pick(mode);
    };

    return (
        <Column style={s.container}>
            {/* No "NETWORK MODE" caption — the three mode buttons name themselves,
                and the rail's height budget is tight (see NetworkPanel). */}
            <Row gap={theme.spacing.xs} style={s.buttonsRow}>
                <ModeButton
                    mode={InternetMode.Open}
                    currentMode={modeChoice.shown}
                    label={l.t("UI_NET_OPEN")}
                    icon={<IconGlobe />}
                    color={theme.colors.success}
                    disabled={disabled || internetModePending}
                    onClick={() => pickMode(InternetMode.Open)}
                />
                <ModeButton
                    mode={InternetMode.Firewall}
                    currentMode={modeChoice.shown}
                    label={l.t("UI_NET_FIREWALL")}
                    icon={<IconShield />}
                    color={accents.resilience.accent}
                    disabled={disabled || internetModePending}
                    onClick={() => pickMode(InternetMode.Firewall)}
                />
                <ModeButton
                    mode={InternetMode.Blackout}
                    currentMode={modeChoice.shown}
                    label={l.t("UI_NET_BLACKOUT")}
                    icon={<IconLightning />}
                    color={accents.crisis.accent}
                    disabled={disabled || internetModePending}
                    onClick={() => pickMode(InternetMode.Blackout)}
                />
            </Row>
            {(internetModePending || internetModeError) && (
                <div style={s.requestHint}>
                    {internetModePending ? l.t("UI_PROCESSING") : l.tDynamic(internetModeError)}
                </div>
            )}

            {/* All three stats render UNCONDITIONALLY as three equal columns — no
                conditional interface: a stat that can appear is always on screen (a
                zero commerce penalty reads as a muted 0%), and the fixed 3-column
                split fits the rail width at any value/locale, so nothing ever clips. */}
            {/* Explicit basis 0 — the `flex: 1` shorthand resolves its basis as auto in
                cohtml, so columns would size to their own text instead of thirds. */}
            <Row style={{ width: "100%" }}>
                <Column style={{ flexGrow: 1, flexShrink: 1, flexBasis: 0, minWidth: 0, alignItems: "flex-start" }}>
                    <span style={s.statLabel}>{l.t("UI_CW_INFECTION")}</span>
                    <span style={{ ...s.statValue, color: infectionColor }}>{formatRate(infectionRate)}</span>
                </Column>
                <Column style={{ flexGrow: 1, flexShrink: 1, flexBasis: 0, minWidth: 0, alignItems: "center" }}>
                    <span style={s.statLabel}>{l.t("UI_CW_RECOVERY")}</span>
                    <span style={{ ...s.statValue, color: recoveryColor }}>{formatRate(recoveryRate)}</span>
                </Column>
                <Column style={{ flexGrow: 1, flexShrink: 1, flexBasis: 0, minWidth: 0, alignItems: "flex-end" }}>
                    <span style={s.statLabel}>{l.t("UI_CW_COMMERCE_STAT")}</span>
                    <span style={{ ...s.statValue, color: penaltyColor }}>{penalty > 0 ? `-${Math.round(penalty * 100)}%` : "0%"}</span>
                </Column>
            </Row>
        </Column>
    );
});

GlobalNetSection.displayName = "GlobalNetSection";
